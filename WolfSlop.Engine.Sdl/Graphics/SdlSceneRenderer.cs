using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using SDL3;
using SkiaSharp;
using WolfSlop.Engine.Assets;
using WolfSlop.Engine.Graphics;
using WolfSlop.Engine.World;

namespace WolfSlop.Engine.Sdl.Graphics;

/// <summary>SDL GPU raycasting, textured surfaces, and ordered sprite rendering.</summary>
public sealed unsafe class SdlSceneRenderer : ISceneRenderer, IDisposable
{
    private readonly GpuDevice _gpu;
    private readonly IAssetSource _assets;
    private readonly RendererSettings _settings;
    private readonly List<nint> _pipelines = [], _shaders = [], _samplers = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<SKBitmap> _overlays = [];
    private SKBitmap? _composite;
    private nint _compute, _walls, _sprites, _uiPipeline;
    private nint _cells, _materials, _spriteBuffer, _rays, _textures, _sky, _world, _uiTexture;
    private nint _clamp, _repeat, _skySampler;
    private uint _layers;
    private int _spriteCapacity;
    private RenderMap? _uploadedMap;
    private RenderFrame _frame;
    private int _phase;
    private bool _fresh = true;
    private int _width, _height, _uiWidth, _uiHeight;
    public string Driver => SDL.GetGPUDeviceDriver(_gpu.Handle) ?? "unknown";

    public SdlSceneRenderer(GpuDevice gpu, IAssetSource assets, RendererSettings settings)
    {
        _gpu = gpu; _assets = assets; _settings = settings;
        _compute = gpu.Compute("raycast");
        nint Shader(string file, string entry, SDL.GPUShaderStage stage, uint samplers, uint buffers, uint uniforms)
        { var shader = gpu.Shader(file, entry, stage, samplers, buffers, uniforms); _shaders.Add(shader); return shader; }
        var fullscreen = Shader("fullscreen", "main", SDL.GPUShaderStage.Vertex, 0, 0, 0);
        _walls = gpu.Pipeline(fullscreen, Shader("walls", "main", SDL.GPUShaderStage.Fragment, 3, 3, 3), SDL.GPUTextureFormat.R8G8B8A8Unorm);
        _sprites = gpu.Pipeline(Shader("sprites-vertex", "vertexMain", SDL.GPUShaderStage.Vertex, 0, 1, 1),
            Shader("sprites-fragment", "fragmentMain", SDL.GPUShaderStage.Fragment, 1, 2, 2), SDL.GPUTextureFormat.R8G8B8A8Unorm, true);
        _uiPipeline = gpu.Pipeline(fullscreen, Shader("ui", "main", SDL.GPUShaderStage.Fragment, 1, 0, 1), SDL.GetGPUSwapchainTextureFormat(gpu.Handle, gpu.Window), true);
        _pipelines.AddRange([_walls, _sprites, _uiPipeline]);
        UpdateFiltering();
        Resize();
    }

    private void UpdateFiltering()
    {
        if (OperatingSystem.IsAndroid()) _settings.MaxAnisotropy = 1;
        foreach (var sampler in _samplers) SDL.ReleaseGPUSampler(_gpu.Handle, sampler);
        _samplers.Clear();
        nint Create(SDL.GPUSamplerAddressMode u, SDL.GPUSamplerAddressMode v)
        {
            var sampler = GpuDevice.Require(SDL.CreateGPUSampler(_gpu.Handle, new() {
                MinFilter = SDL.GPUFilter.Linear, MagFilter = SDL.GPUFilter.Linear, MipmapMode = SDL.GPUSamplerMipmapMode.Linear,
                AddressModeU = u, AddressModeV = v, AddressModeW = SDL.GPUSamplerAddressMode.ClampToEdge,
                EnableAnisotropy = _settings.MaxAnisotropy > 1, MaxAnisotropy = _settings.MaxAnisotropy, MaxLod = 20 }));
            _samplers.Add(sampler); return sampler;
        }
        _clamp = Create(SDL.GPUSamplerAddressMode.ClampToEdge, SDL.GPUSamplerAddressMode.ClampToEdge);
        _repeat = Create(SDL.GPUSamplerAddressMode.Repeat, SDL.GPUSamplerAddressMode.Repeat);
        _skySampler = Create(SDL.GPUSamplerAddressMode.Repeat, SDL.GPUSamplerAddressMode.ClampToEdge);
    }

    public void ApplySettings(RendererSettings settings)
    {
        settings.Validate();
        SDL.WaitForGPUIdle(_gpu.Handle);
        _settings.Apply(settings); UpdateFiltering(); Resize(); _fresh = true;
    }

    private void Resize()
    {
        if (_width == _settings.Width && _height == _settings.Height) return;
        ReleaseTexture(ref _world); ReleaseBuffer(ref _rays);
        _width = _settings.Width; _height = _settings.Height;
        _world = _gpu.Texture((uint)_width, (uint)_height, target: true);
        _rays = _gpu.Buffer((uint)_width * 16, SDL.GPUBufferUsageFlags.ComputeStorageWrite | SDL.GPUBufferUsageFlags.GraphicsStorageRead);
        _fresh = true;
    }

    public Task PrepareSceneAsync(RenderMap map, CancellationToken cancellationToken = default,
        Func<string, Task>? reportProgress = null, SceneResources? resources = null)
    {
        ReleaseScene();
        var command = GpuDevice.Require(SDL.AcquireGPUCommandBuffer(_gpu.Handle));
        try
        {
            foreach (var path in resources?.OverlayTextures ?? [])
            { cancellationToken.ThrowIfCancellationRequested(); _overlays.Add(Decode(path)); }
            var catalog = resources?.Textures ?? map.Textures;
            _layers = catalog.LayerCount;
            var images = new Dictionary<string, SKBitmap>();
            try
            {
                foreach (var path in catalog.Layers.Values.Where(p => p != null).Distinct())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    images[path!] = AssetImage.Decode(_assets, path!);
                }
                var largest = catalog.Layers.Max(pair => pair.Value == null ? 64 : catalog.Frames.TryGetValue(pair.Key, out var crop)
                    ? Math.Max(crop.Width, crop.Height) : Math.Max(images[pair.Value].Width, images[pair.Value].Height));
                var limit = (int)Math.Sqrt(128 * 1024 * 1024 / (4.0 * _layers * 4 / 3));
                var size = 1; while (size * 2 <= Math.Min(2048, limit) && size < largest) size *= 2;
                var levels = (uint)BitOperations.Log2((uint)size) + 1;
                _textures = _gpu.Texture((uint)size, (uint)size, Math.Max(2, _layers), levels);
                for (uint layer = 0; layer < Math.Max(2, _layers); layer++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
                    using (var canvas = new SKCanvas(bitmap))
                    {
                        canvas.Clear(SKColors.Magenta);
                        if (catalog.Layers.TryGetValue(layer, out var path) && path != null)
                        {
                            var image = images[path];
                            var source = catalog.Frames.TryGetValue(layer, out var crop)
                                ? new SKRect(crop.X, crop.Y, crop.X + crop.Width, crop.Y + crop.Height) : new SKRect(0, 0, image.Width, image.Height);
                            canvas.Clear(SKColors.Transparent);
                            canvas.DrawBitmap(image, source, new SKRect(0, 0, size, size));
                        }
                    }
                    UploadMips(command, _textures, bitmap, layer, levels);
                }
            }
            finally { foreach (var image in images.Values) image.Dispose(); }
            using (var bitmap = map.Sky is { } sky ? Decode(sky.Texture) : new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul))
            {
                _sky = _gpu.Texture((uint)bitmap.Width, (uint)bitmap.Height);
                _gpu.UploadTexture(command, _sky, bitmap.GetPixels(), (uint)bitmap.Width, (uint)bitmap.Height);
            }
            _cells = _gpu.Buffer((uint)map.Cells.Count * 16, SDL.GPUBufferUsageFlags.ComputeStorageRead | SDL.GPUBufferUsageFlags.GraphicsStorageRead);
            _materials = _gpu.Buffer((uint)map.Materials.Count * 16, SDL.GPUBufferUsageFlags.GraphicsStorageRead);
            _gpu.Upload(command, _materials, MemoryMarshal.AsBytes(map.Materials.ToArray().AsSpan()));
            UploadMap(command, map);
            GpuDevice.Check(SDL.SubmitGPUCommandBuffer(command)); command = 0;
            _clock.Restart(); _fresh = true;
            Console.WriteLine($"[GPU] Prepared {map.Width}x{map.Height}, {map.Sprites.Count} sprites, {_layers} texture layers");
            return Task.CompletedTask;
        }
        catch { if (command != 0) { SDL.CancelGPUCommandBuffer(command); command = 0; } ReleaseScene(); throw; }
        finally { if (command != 0) SDL.CancelGPUCommandBuffer(command); }
    }

    private SKBitmap Decode(string path) => AssetImage.Decode(_assets, path);
    private void UploadMips(nint command, nint texture, SKBitmap bitmap, uint layer, uint levels)
    {
        _gpu.UploadTexture(command, texture, bitmap.GetPixels(), (uint)bitmap.Width, (uint)bitmap.Height, layer);
        for (uint level = 1; level < levels; level++)
        {
            using var mip = bitmap.Resize(new SKImageInfo(Math.Max(1, bitmap.Width >> (int)level), Math.Max(1, bitmap.Height >> (int)level), SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            _gpu.UploadTexture(command, texture, mip.GetPixels(), (uint)mip.Width, (uint)mip.Height, layer, level);
        }
    }
    private void UploadMap(nint command, RenderMap map)
    {
        if (ReferenceEquals(_uploadedMap, map)) return;
        _gpu.Upload(command, _cells, MemoryMarshal.AsBytes(map.Cells.ToArray().AsSpan()));
        _uploadedMap = map;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct GpuSprite(Vector2 Position, uint Layer, uint Owner, Vector2 Scale, float Elevation, uint Kind);
    public void Render(RenderFrame frame) => _frame = frame;

    /// <summary>Submit the queued world and premultiplied UI together on the window thread.</summary>
    public bool Present(SKBitmap ui)
    {
        ui = CompositeUi(ui);
        var command = GpuDevice.Require(SDL.AcquireGPUCommandBuffer(_gpu.Handle));
        try
        {
            GpuDevice.Check(SDL.WaitAndAcquireGPUSwapchainTexture(command, _gpu.Window, out var swapchain, out var width, out var height));
            if (swapchain == 0) { SDL.CancelGPUCommandBuffer(command); command = 0; return false; }
            var frame = _frame;
            UploadMap(command, frame.Map);
            var visible = frame.Map.Sprites.Concat(frame.DynamicSprites).Where(sprite =>
            {
                var relative = sprite.Position - frame.Position;
                var radius = sprite.MaximumViewWidth * .5f;
                return (_settings.RenderDistance <= 0 || relative.Length() - radius <= _settings.RenderDistance) &&
                    Vector2.Dot(relative, frame.Direction) + radius > 0;
            }).OrderByDescending(sprite => Vector2.DistanceSquared(sprite.Position, frame.Position)).Select(sprite =>
            {
                var index = sprite.ViewFrame(frame.Position, _clock.Elapsed.TotalSeconds);
                return new GpuSprite(sprite.Position, sprite.TextureLayer + (uint)index, sprite.OwnerCell,
                    new(sprite.ViewWidth(index), sprite.Scale.Y), sprite.Elevation, sprite.ViewKind(index));
            }).ToArray();
            if (visible.Length > _spriteCapacity || _spriteBuffer == 0)
            {
                ReleaseBuffer(ref _spriteBuffer); _spriteCapacity = Math.Max(16, visible.Length * 2);
                _spriteBuffer = _gpu.Buffer((uint)_spriteCapacity * 32, SDL.GPUBufferUsageFlags.GraphicsStorageRead);
            }
            _gpu.Upload(command, _spriteBuffer, MemoryMarshal.AsBytes(visible.AsSpan()));
            if (_uiWidth != ui.Width || _uiHeight != ui.Height)
            {
                ReleaseTexture(ref _uiTexture); _uiWidth = ui.Width; _uiHeight = ui.Height;
                _uiTexture = _gpu.Texture((uint)ui.Width, (uint)ui.Height);
            }
            _gpu.UploadTexture(command, _uiTexture, ui.GetPixels(), (uint)ui.Width, (uint)ui.Height);
            var step = _fresh ? 1 : _settings.ColumnInterlaceFactor;
            var camera = new float[] { frame.Position.X, frame.Position.Y, frame.Direction.X, frame.Direction.Y,
                frame.Plane.X, frame.Plane.Y, _width, _height, frame.Pitch, frame.Elevation,
                _settings.RenderDistance, _settings.FogStartDistance, step, _phase % step, 0, 0 };
            var mapInfo = new uint[] { (uint)frame.Map.Width, (uint)frame.Map.Height, (uint)visible.Length, 0 };
            var cameraBytes = MemoryMarshal.AsBytes(camera.AsSpan()); var mapBytes = MemoryMarshal.AsBytes(mapInfo.AsSpan());
            SDL.PushGPUComputeUniformData(command, 0, cameraBytes, (uint)cameraBytes.Length);
            SDL.PushGPUComputeUniformData(command, 1, mapBytes, (uint)mapBytes.Length);
            var compute = SDL.BeginGPUComputePass(command, ReadOnlySpan<SDL.GPUStorageTextureReadWriteBinding>.Empty, 0,
                [new SDL.GPUStorageBufferReadWriteBinding { Buffer = _rays }], 1);
            SDL.BindGPUComputePipeline(compute, _compute);
            SDL.BindGPUComputeStorageBuffers(compute, 0, new[] { _cells }, 1);
            SDL.DispatchGPUCompute(compute, (uint)((_width + step * 64 - 1) / (step * 64)), 1, 1);
            SDL.EndGPUComputePass(compute);
            SDL.PushGPUFragmentUniformData(command, 0, cameraBytes, (uint)cameraBytes.Length);
            SDL.PushGPUFragmentUniformData(command, 1, mapBytes, (uint)mapBytes.Length);
            var sky = new uint[] { BitConverter.SingleToUInt32Bits((frame.Map.Sky?.RotationDegrees ?? 0) / 360),
                BitConverter.SingleToUInt32Bits(frame.Map.Sky == null ? 0 : 1), 0, 0, _layers, 0, 0, 0 };
            SDL.PushGPUFragmentUniformData(command, 2, MemoryMarshal.AsBytes(sky.AsSpan()), 32);
            SDL.PushGPUVertexUniformData(command, 0, cameraBytes, (uint)cameraBytes.Length);
            var pass = SDL.BeginGPURenderPass(command, [new SDL.GPUColorTargetInfo { Texture = _world,
                LoadOp = _fresh ? SDL.GPULoadOp.Clear : SDL.GPULoadOp.Load, StoreOp = SDL.GPUStoreOp.Store }], 1, 0);
            SDL.BindGPUGraphicsPipeline(pass, _walls);
            SDL.BindGPUFragmentSamplers(pass, 0, new[] { Binding(_textures, _clamp), Binding(_textures, _repeat), Binding(_sky, _skySampler) }, 3);
            SDL.BindGPUFragmentStorageBuffers(pass, 0, new[] { _rays, _cells, _materials }, 3);
            SDL.DrawGPUPrimitives(pass, 3, 1, 0, 0);
            SDL.BindGPUGraphicsPipeline(pass, _sprites);
            SDL.BindGPUVertexStorageBuffers(pass, 0, new[] { _spriteBuffer }, 1);
            SDL.BindGPUFragmentSamplers(pass, 0, new[] { Binding(_textures, _clamp) }, 1);
            SDL.BindGPUFragmentStorageBuffers(pass, 0, new[] { _rays, _cells }, 2);
            SDL.DrawGPUPrimitives(pass, 6, (uint)visible.Length, 0, 0);
            SDL.EndGPURenderPass(pass);
            SDL.BlitGPUTexture(command, new SDL.GPUBlitInfo {
                Source = new() { Texture = _world, W = (uint)_width, H = (uint)_height },
                Destination = new() { Texture = swapchain, W = width, H = height }, Filter = SDL.GPUFilter.Linear, LoadOp = SDL.GPULoadOp.DontCare });
            var dimensions = new float[] { width, height, 0, 0 };
            SDL.PushGPUFragmentUniformData(command, 0, MemoryMarshal.AsBytes(dimensions.AsSpan()), 16);
            pass = SDL.BeginGPURenderPass(command, [new SDL.GPUColorTargetInfo { Texture = swapchain, LoadOp = SDL.GPULoadOp.Load, StoreOp = SDL.GPUStoreOp.Store }], 1, 0);
            SDL.BindGPUGraphicsPipeline(pass, _uiPipeline);
            SDL.BindGPUFragmentSamplers(pass, 0, new[] { Binding(_uiTexture, _clamp) }, 1);
            SDL.DrawGPUPrimitives(pass, 3, 1, 0, 0); SDL.EndGPURenderPass(pass);
            GpuDevice.Check(SDL.SubmitGPUCommandBuffer(command)); command = 0;
            _fresh = false; _phase = (_phase + 1) % _settings.ColumnInterlaceFactor;
            return true;
        }
        finally { if (command != 0) SDL.CancelGPUCommandBuffer(command); }
    }
    private static SDL.GPUTextureSamplerBinding Binding(nint texture, nint sampler) => new() { Texture = texture, Sampler = sampler };
    private SKBitmap CompositeUi(SKBitmap ui)
    {
        if (_frame.Overlays.Count == 0) return ui;
        if (_composite == null || _composite.Width != ui.Width || _composite.Height != ui.Height)
        { _composite?.Dispose(); _composite = new(new SKImageInfo(ui.Width, ui.Height, SKColorType.Rgba8888, SKAlphaType.Premul)); }
        using var canvas = new SKCanvas(_composite); canvas.Clear(SKColors.Transparent);
        foreach (var overlay in _frame.Overlays)
        {
            if ((uint)overlay.Texture >= _overlays.Count) throw new InvalidDataException("Screen overlay references an unloaded texture.");
            var image = _overlays[overlay.Texture]; var size = overlay.Size * new Vector2(ui.Width, ui.Height);
            var origin = -overlay.Pivot * size; var uv = overlay.UV;
            canvas.Save(); canvas.Translate(overlay.Position.X * ui.Width, overlay.Position.Y * ui.Height); canvas.RotateRadians(overlay.Rotation);
            canvas.DrawBitmap(image, new SKRect(uv.X * image.Width, uv.Y * image.Height, (uv.X + uv.Z) * image.Width, (uv.Y + uv.W) * image.Height),
                new SKRect(origin.X, origin.Y, origin.X + size.X, origin.Y + size.Y));
            canvas.Restore();
        }
        canvas.DrawBitmap(ui, 0, 0); return _composite;
    }
    public void SaveScreenshot(string path, SKBitmap ui)
    {
        ui = CompositeUi(ui);
        var transfer = GpuDevice.Require(SDL.CreateGPUTransferBuffer(_gpu.Handle, new() {
            Size = (uint)(_width * _height * 4), Usage = SDL.GPUTransferBufferUsage.Download }));
        nint fence = 0;
        try
        {
            var command = GpuDevice.Require(SDL.AcquireGPUCommandBuffer(_gpu.Handle));
            var copy = SDL.BeginGPUCopyPass(command);
            SDL.DownloadFromGPUTexture(copy, new SDL.GPUTextureRegion { Texture = _world, W = (uint)_width, H = (uint)_height, D = 1 },
                new SDL.GPUTextureTransferInfo { TransferBuffer = transfer, PixelsPerRow = (uint)_width, RowsPerLayer = (uint)_height });
            SDL.EndGPUCopyPass(copy);
            fence = GpuDevice.Require(SDL.SubmitGPUCommandBufferAndAcquireFence(command));
            GpuDevice.Check(SDL.WaitForGPUFences(_gpu.Handle, true, new[] { fence }, 1));
            var pointer = GpuDevice.Require(SDL.MapGPUTransferBuffer(_gpu.Handle, transfer, false));
            using var world = new SKBitmap(new SKImageInfo(_width, _height, SKColorType.Rgba8888, SKAlphaType.Premul));
            new ReadOnlySpan<byte>((void*)pointer, _width * _height * 4).CopyTo(new Span<byte>((void*)world.GetPixels(), _width * _height * 4));
            SDL.UnmapGPUTransferBuffer(_gpu.Handle, transfer);
            // Validate actual GPU output, independently of the HUD.
            var colors = new HashSet<uint>();
            var pixels = new ReadOnlySpan<uint>((void*)world.GetPixels(), _width * _height);
            for (var i = 0; i < pixels.Length; i += 13) colors.Add(pixels[i]);
            if (colors.Count < 32) throw new InvalidDataException($"GPU output has only {colors.Count} distinct colors.");
            using var result = new SKBitmap(new SKImageInfo(ui.Width, ui.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(result)) { canvas.DrawBitmap(world, new SKRect(0, 0, ui.Width, ui.Height)); canvas.DrawBitmap(ui, 0, 0); }
            using var image = SKImage.FromBitmap(result); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); using var output = File.Create(path); encoded.SaveTo(output);
        }
        finally { if (fence != 0) SDL.ReleaseGPUFence(_gpu.Handle, fence); SDL.ReleaseGPUTransferBuffer(_gpu.Handle, transfer); }
    }
    private void ReleaseTexture(ref nint texture) { if (texture != 0) SDL.ReleaseGPUTexture(_gpu.Handle, texture); texture = 0; }
    private void ReleaseBuffer(ref nint buffer) { if (buffer != 0) SDL.ReleaseGPUBuffer(_gpu.Handle, buffer); buffer = 0; }
    public void ReleaseScene()
    {
        SDL.WaitForGPUIdle(_gpu.Handle);
        ReleaseBuffer(ref _cells); ReleaseBuffer(ref _materials); ReleaseBuffer(ref _spriteBuffer);
        ReleaseTexture(ref _textures); ReleaseTexture(ref _sky); _uploadedMap = null; _spriteCapacity = 0; _fresh = true;
        foreach (var overlay in _overlays) overlay.Dispose(); _overlays.Clear();
    }
    public void Dispose()
    {
        ReleaseScene(); ReleaseBuffer(ref _rays); ReleaseTexture(ref _world); ReleaseTexture(ref _uiTexture);
        foreach (var item in _pipelines) SDL.ReleaseGPUGraphicsPipeline(_gpu.Handle, item);
        foreach (var item in _shaders) SDL.ReleaseGPUShader(_gpu.Handle, item);
        foreach (var item in _samplers) SDL.ReleaseGPUSampler(_gpu.Handle, item);
        SDL.ReleaseGPUComputePipeline(_gpu.Handle, _compute);
        _composite?.Dispose();
    }
}
