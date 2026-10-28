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
    private const int CameraBufferSize = 48;
    private const int MapInfoBufferSize = 16;
    private const int SpriteSize = 32;

    private readonly RendererSettings _settings;
    private readonly BlazorJSRuntime _js;

    private GPUDevice? _device;
    private GPUCanvasContext? _context;

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
    private readonly SpriteSelection _spriteSelection = new();
    private BrowserSpriteSelection? _browserSprites;
    private byte[] _spriteBytes = [];
    private Uint32Array? _spriteUpload;
    private Float32Array? _cameraUpload;
    private Uint32Array? _spriteCountUpload;
    private readonly byte[] _cameraBytes = new byte[CameraBufferSize];
    private readonly byte[] _spriteCountBytes = new byte[4];
    private int _uploadedSpriteCount = -1;
    private bool _profiling;
    private long _profileSampleStart;
    private int _profileFrames;
    private double _selectionMilliseconds, _uploadMilliseconds, _submissionMilliseconds;
    private long _profileCandidates, _profileSprites;
    private readonly Stopwatch _sceneAnimationClock = new();

    private GPUBindGroup? _computeBindGroup;
    private GPUBindGroup? _renderBindGroup;
    private GpuSceneTextures? _textures;

    // Reusable CPU buffers.
    private readonly float[] _cameraData = new float[12];
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
        using var queue = _device.Queue;
        UpdateMap(map, queue);

        // 2. Update camera every frame.
        UpdateCamera(position, direction, plane, pitch, elevation, queue);
        UpdateOverlays(frame.Overlays);
        var selectionStart = _profiling ? Stopwatch.GetTimestamp() : 0;
        _browserSprites!.Select(_spriteSelection, map.Sprites, position, direction, plane, _settings.RenderDistance,
            frame.CullSpritesOutsideView, _sceneAnimationClock.Elapsed.TotalSeconds, frame.DynamicSprites);
        var uploadStart = _profiling ? Stopwatch.GetTimestamp() : 0;
        UpdateSprites(queue);
        var submissionStart = _profiling ? Stopwatch.GetTimestamp() : 0;

        // 3. Create GPU command encoder.
        using var encoder =
            _device.CreateCommandEncoder();

        // 4. Compute pass: one DDA ray per screen column.
        using (var compute = encoder.BeginComputePass(
                   new GPUComputePassDescriptor
                   {
                       Label = "WolfNET DDA Compute Pass"
                   }))
        {
            compute.SetPipeline(_computePipeline);

            compute.SetBindGroup(
                0,
                _computeBindGroup);

            uint workgroups =
                ((uint)_settings.Width + WorkgroupSize - 1)
                / WorkgroupSize;

            compute.DispatchWorkgroups(workgroups);

            compute.End();
        }

        // 5. Acquire the current canvas texture.
        using var canvasTexture =
            _context.GetCurrentTexture();

        using var canvasView =
            canvasTexture.CreateView();

        // 6. Render pass: shade pixels using RayHit[].
        using (var pass = encoder.BeginRenderPass(
                   new GPURenderPassDescriptor
                   {
                       ColorAttachments =
                       [
                           new GPURenderPassColorAttachment
                           {
                               View = canvasView,

                               ClearValue = new double[]
                               {
                                   0.0,
                                   0.0,
                                   0.0,
                                   1.0
                               },

                               LoadOp = GPULoadOp.Clear,
                               StoreOp = GPUStoreOp.Store
                           }
                       ]
                   }))
        {
            pass.SetPipeline(_renderPipeline);

            pass.SetBindGroup(
                0,
                _renderBindGroup);

            // Fullscreen triangle.
            pass.Draw(3);

            if (_spriteSelection.Visible.Length > 0)
            {
                pass.SetPipeline(_spritePipeline);
                pass.SetBindGroup(0, _spriteBindGroup!);
                pass.Draw(6, (uint)_spriteSelection.Visible.Length);
            }

            DrawOverlays(pass, frame.Overlays);

            if (frame.ShowMinimap)
            {
                pass.SetPipeline(_minimapPipeline);
                pass.SetBindGroup(0, _minimapBindGroup!);
                pass.Draw(3);
            }

            pass.End();
        }

        // 7. Submit compute and render passes.
        using var commands = encoder.Finish();

        queue.Submit([commands]);
        if (_profiling) ReportProfile(selectionStart, uploadStart, submissionStart);

        _frameCount++;

        if (_frameCount == 1 ||
            _frameCount % 1000 == 0)
        {
            Console.WriteLine(
                $"[GPU] Frame {_frameCount}");
        }
    }

    private void ReportProfile(long selectionStart, long uploadStart, long submissionStart)
    {
        _selectionMilliseconds += Stopwatch.GetElapsedTime(selectionStart, uploadStart).TotalMilliseconds;
        _uploadMilliseconds += Stopwatch.GetElapsedTime(uploadStart, submissionStart).TotalMilliseconds;
        _submissionMilliseconds += Stopwatch.GetElapsedTime(submissionStart).TotalMilliseconds;
        _profileCandidates += _spriteSelection.CandidatesExamined;
        _profileSprites += _spriteSelection.Visible.Length;
        _profileFrames++;
        if (Stopwatch.GetElapsedTime(_profileSampleStart).TotalSeconds < 1) return;
        Console.WriteLine("[PROFILE] " + System.Text.Json.JsonSerializer.Serialize(new
        {
            frames = _profileFrames,
            candidates = _profileCandidates / (double)_profileFrames,
            sprites = _profileSprites / (double)_profileFrames,
            uploadBytes = _profileSprites * SpriteSize / (double)_profileFrames,
            selectionMs = _selectionMilliseconds / _profileFrames,
            uploadMs = _uploadMilliseconds / _profileFrames,
            // CPU command encoding/submission time; this does not wait for GPU completion.
            submissionMs = _submissionMilliseconds / _profileFrames
        }));
        _profileSampleStart = Stopwatch.GetTimestamp();
        _profileFrames = 0;
        _selectionMilliseconds = _uploadMilliseconds = _submissionMilliseconds = 0;
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
        _overlayPipeline?.Dispose();

        _context?.Dispose();
        _cameraUpload?.Dispose();
        _spriteCountUpload?.Dispose();
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
