using System.Diagnostics;
using System.Numerics;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Engine.World;
using WolfNET.Engine.Actors;

using WolfNET.Engine.Graphics;

namespace WolfNET.Engine.Browser.Graphics;

public sealed partial class WebGpuRenderer : ISceneRenderer, IDisposable
{
    private const uint WorkgroupSize = 64;
    private const int RayHitSize = 16;
    private const int CameraBufferSize = 64;
    private const int MapInfoBufferSize = 16;
    private const int SpriteSize = 32;

    private readonly RendererSettings _settings;
    private readonly BlazorJSRuntime _js;

    private GPUDevice? _device;
    private GPUCanvasContext? _context;
    private JSObject? _frameSubmission;

    private GPUComputePipeline? _computePipeline;
    private GPURenderPipeline? _renderPipeline;
    private GPURenderPipeline? _spritePipeline;
    private GPURenderPipeline? _minimapPipeline;
    private GPUBindGroup? _minimapBindGroup;

    private GPUBuffer? _cameraBuffer;
    private GPUBuffer? _mapInfoBuffer;
    private GPUBuffer? _worldMapBuffer;
    private GPUBuffer? _rayHitsBuffer;
    private GPUBuffer? _spriteBuffer;
    private GPUBindGroup? _spriteBindGroup;
    private int _spriteCapacity;
    private int _spriteCount;
    private BrowserSpriteSelection? _browserSprites;
    private readonly byte[] _cameraBytes = new byte[CameraBufferSize];
    private bool _profiling;
    private long _profileSampleStart;
    private int _profileFrames;
    private double _selectionMilliseconds, _uploadMilliseconds, _submissionMilliseconds, _batchedMilliseconds;
    private long _profileCandidates, _profileSprites;
    private readonly Stopwatch _sceneAnimationClock = new();

    private GPUBindGroup? _computeBindGroup;
    private GPUBindGroup? _renderBindGroup;
    private GpuSceneTextures? _textures;

    // Reusable CPU buffers.
    private readonly float[] _cameraData = new float[16];
    private GPUBuffer? _materialsBuffer;
    private TextureCatalog? _preparedTextures;
    private RenderMap? _uploadedMap;

    private bool _initialized;
    private bool _disposed;
    private int _frameCount;

    public WebGpuRenderer(
        RendererSettings settings,
        BlazorJSRuntime js)
    {
        _settings = settings;
        _js = js;
    }

    /// <summary>
    /// Executes compute raycasting and fragment rendering.
    /// Map buffers are synchronized only when needed.
    /// </summary>
    public void Render(RenderFrame frame)
    {
        var (position, direction, plane, map, pitch, elevation) = frame;
        if (!_initialized ||
            _device == null ||
            _context == null ||
            _computePipeline == null ||
            _renderPipeline == null ||
            _spritePipeline == null ||
            _minimapPipeline == null ||
            _computeBindGroup == null ||
            _renderBindGroup == null)
        {
            return;
        }

        // 1. Synchronize map only if revision changed.
        if (!ReferenceEquals(_uploadedMap, map))
        {
            using var queue = _device.Queue;
            UpdateMap(map, queue);
        }

        // 2. A new scene or mode starts with a complete frame before rotating columns.
        var (columnStep, columnPhase) = PrepareColumnFrame();
        UpdateCamera(position, direction, plane, pitch, elevation, columnStep, columnPhase);
        UpdateOverlays(frame.Overlays);
        uint columns = ((uint)_settings.Width + (uint)columnStep - 1 - (uint)columnPhase) / (uint)columnStep;
        uint workgroups = (columns + WorkgroupSize - 1) / WorkgroupSize;
        var direct = frame.Overlays.Count == 0 && !frame.ShowMinimap;
        var selectionStart = _profiling ? Stopwatch.GetTimestamp() : 0;
        var selected = _browserSprites!.Select(map.Sprites, position, direction, plane, _settings.RenderDistance,
            frame.CullSpritesOutsideView, _sceneAnimationClock.Elapsed.TotalSeconds, frame.DynamicSprites, _cameraBytes,
            direct ? columnStep : 0, workgroups);
        var submitted = direct && selected >= 0;
        _spriteCount = selected >= 0 ? selected : ~selected;
        var uploadStart = _profiling ? Stopwatch.GetTimestamp() : 0;
        if (!submitted) UpdateSprites();
        var submissionStart = _profiling ? Stopwatch.GetTimestamp() : 0;

        // Encode the GPU frame in one browser call. Per-command WASM interop
        // otherwise costs more CPU time than the skipped rays save on the GPU.
        var submission = _frameSubmission!.JSRef!;
        if (!submitted && direct)
            submission.CallVoid("render", columnStep, workgroups, _spriteCount);
        else if (!submitted)
        {
            using var pass = submission.Call<GPURenderPassEncoder>("begin", columnStep, workgroups, _spriteCount);
            DrawScreenLayers(pass, frame);
            pass.End();
            submission.CallVoid("submit");
        }
        _columnFrameValid = true;
        if (columnStep > 1) _columnPhase = (_columnPhase + 1) % _columnFactor;
        if (_profiling) ReportProfile(selectionStart, uploadStart, submissionStart, submitted);

        _frameCount++;

        if (_frameCount == 1 ||
            _frameCount % 1000 == 0)
        {
            Console.WriteLine(
                $"[GPU] Frame {_frameCount}");
        }
    }

    private void ReportProfile(long selectionStart, long uploadStart, long submissionStart, bool batched)
    {
        if (batched) _batchedMilliseconds += Stopwatch.GetElapsedTime(selectionStart, uploadStart).TotalMilliseconds;
        else _selectionMilliseconds += Stopwatch.GetElapsedTime(selectionStart, uploadStart).TotalMilliseconds;
        _uploadMilliseconds += Stopwatch.GetElapsedTime(uploadStart, submissionStart).TotalMilliseconds;
        _submissionMilliseconds += Stopwatch.GetElapsedTime(submissionStart).TotalMilliseconds;
        _profileCandidates += _browserSprites!.CandidatesExamined;
        _profileSprites += _spriteCount;
        _profileFrames++;
        if (Stopwatch.GetElapsedTime(_profileSampleStart).TotalSeconds < 1) return;
        Console.WriteLine("[PROFILE] " + System.Text.Json.JsonSerializer.Serialize(new
        {
            frames = _profileFrames,
            candidates = _profileCandidates / (double)_profileFrames,
            sprites = _profileSprites / (double)_profileFrames,
            uploadBytes = _profileSprites * SpriteSize / (double)_profileFrames,
            selectionMs = _selectionMilliseconds / _profileFrames,
            selectionAndSubmitMs = _batchedMilliseconds / _profileFrames,
            uploadMs = _uploadMilliseconds / _profileFrames,
            // CPU command encoding/submission time; this does not wait for GPU completion.
            submissionMs = _submissionMilliseconds / _profileFrames
        }));
        _profileSampleStart = Stopwatch.GetTimestamp();
        _profileFrames = 0;
        _selectionMilliseconds = _uploadMilliseconds = _submissionMilliseconds = _batchedMilliseconds = 0;
        _profileCandidates = _profileSprites = 0;
    }

    private void OnGpuError(
        GPUUncapturedErrorEvent e)
    {
        Console.Error.WriteLine(
            $"[GPU] Uncaptured error: {e.Error.Message}");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _initialized = false;

        Console.WriteLine("[GPU] Disposing renderer");

        if (_device != null)
        {
            _device.OnUncapturedError -= OnGpuError;
        }

        _computeBindGroup?.Dispose();
        ReleaseScene();

        DestroyBuffer(_cameraBuffer);
        DestroyBuffer(_mapInfoBuffer);
        DestroyBuffer(_worldMapBuffer);
        DestroyBuffer(_rayHitsBuffer);

        _computePipeline?.Dispose();
        _renderPipeline?.Dispose();
        _spritePipeline?.Dispose();
        _minimapPipeline?.Dispose();
        _presentPipeline?.Dispose();
        _overlayPipeline?.Dispose();

        _context?.Dispose();
        _frameSubmission?.Dispose();
        _frameSubmission = null;
        _browserSprites?.Dispose();
        _browserSprites = null;
        _device?.Dispose();

        _uploadedMap = null;

        Console.WriteLine("[GPU] Renderer disposed");
    }

    private static void DestroyBuffer(GPUBuffer? buffer)
    {
        if (buffer == null)
            return;

        buffer.Destroy();
        buffer.Dispose();
    }
}
