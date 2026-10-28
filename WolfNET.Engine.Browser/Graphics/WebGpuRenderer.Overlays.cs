using System.Runtime.InteropServices;
using SpawnDev.BlazorJS.JSObjects;

using WolfNET.Engine.Graphics;

namespace WolfNET.Engine.Browser.Graphics;

public sealed partial class WebGpuRenderer
{
    private sealed record OverlayImage(GPUTexture Texture, GPUTextureView View, GPUBindGroup BindGroup);
    private GPURenderPipeline? _overlayPipeline;
    private GPUBuffer? _overlayBuffer;
    private GPUSampler? _overlaySampler;
    private Float32Array? _overlayUpload;
    private byte[] _overlayBytes = [];
    private readonly List<OverlayImage> _overlayImages = [];
    private int _overlayCapacity;

    private async Task CreateOverlayPipelineAsync(string source, string format, CancellationToken token)
    {
        using var shader = _device!.CreateShaderModule(new GPUShaderModuleDescriptor
        {
            Label = "WolfNET Overlay Shader", Code = source
        });
        _device.PushErrorScope(GPUErrorFilter.Validation);
        _overlayPipeline = _device.CreateRenderPipeline(new GPURenderPipelineDescriptor
        {
            Label = "WolfNET Overlay Pipeline", Layout = GPUAutoLayoutMode.Auto,
            Vertex = new GPUVertexState { Module = shader, EntryPoint = "vertexMain" },
            Fragment = new GPUFragmentState
            {
                Module = shader, EntryPoint = "fragmentMain",
                Targets = [new GPUColorTargetState
                {
                    Format = format,
                    Blend = new GPUBlendState
                    {
                        Color = new GPUBlendComponent { SrcFactor = "one", DstFactor = "one-minus-src-alpha", Operation = "add" },
                        Alpha = new GPUBlendComponent { SrcFactor = "one", DstFactor = "one-minus-src-alpha", Operation = "add" }
                    }
                }]
            }
        });
        using var error = await _device.PopErrorScope();
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (error != null) throw new InvalidOperationException($"Overlay pipeline validation failed: {error.Message}");
    }

    private async Task PrepareOverlaysAsync(IReadOnlyList<string> paths, CancellationToken token, Func<string, Task>? reportProgress)
    {
        if (paths.Count == 0) return;
        if (reportProgress != null) await reportProgress("Preparing screen overlays…");
        _overlaySampler = _device!.CreateSampler(new GPUSamplerDescriptor
        {
            Label = "WolfNET Overlay Filtering", MinFilter = "linear", MagFilter = "linear",
            AddressModeU = "clamp-to-edge", AddressModeV = "clamp-to-edge"
        });
        // Bind groups are created when the first frame establishes buffer capacity.
        foreach (var path in paths)
        {
            var texture = await LoadOverlayTextureAsync(path, token);
            var view = texture.CreateView();
            _overlayImages.Add(new(texture, view, null!));
        }
    }

    private void UpdateOverlays(IReadOnlyList<ScreenOverlay> layers)
    {
        if (layers.Count == 0) return;
        if (layers.Count > _overlayCapacity)
        {
            _overlayCapacity = Math.Max(layers.Count, Math.Max(4, _overlayCapacity * 2));
            foreach (var image in _overlayImages) image.BindGroup?.Dispose();
            DestroyBuffer(_overlayBuffer);
            _overlayUpload?.Dispose();
            _overlayBytes = new byte[_overlayCapacity * 64];
            _overlayUpload = new Float32Array(_overlayBytes.Length / 4);
            _overlayBuffer = _device!.CreateBuffer(new GPUBufferDescriptor
            {
                Label = "WolfNET Screen Layers", Size = (ulong)_overlayBytes.Length,
                Usage = GPUBufferUsage.Storage | GPUBufferUsage.CopyDst
            });
            using var layout = _overlayPipeline!.GetBindGroupLayout(0);
            for (var i = 0; i < _overlayImages.Count; i++)
            {
                var image = _overlayImages[i];
                var group = _device.CreateBindGroup(new GPUBindGroupDescriptor
                {
                    Layout = layout,
                    Entries = [BufferEntry(0, _overlayBuffer),
                        new GPUBindGroupEntry { Binding = 1, Resource = image.View },
                        new GPUBindGroupEntry { Binding = 2, Resource = _overlaySampler! }]
                });
                _overlayImages[i] = image with { BindGroup = group };
            }
        }
        var data = MemoryMarshal.Cast<byte, float>(_overlayBytes.AsSpan());
        for (var i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            if (layer.Texture < 0 || layer.Texture >= _overlayImages.Count)
                throw new InvalidOperationException("Overlay texture was not included in SceneResources.");
            var offset = i * 16;
            data[offset] = layer.Position.X; data[offset + 1] = layer.Position.Y;
            data[offset + 2] = layer.Size.X; data[offset + 3] = layer.Size.Y;
            data[offset + 4] = layer.UV.X; data[offset + 5] = layer.UV.Y;
            data[offset + 6] = layer.UV.Z; data[offset + 7] = layer.UV.W;
            data[offset + 8] = layer.Pivot.X; data[offset + 9] = layer.Pivot.Y;
            data[offset + 10] = layer.Rotation;
            data[offset + 12] = _settings.Width; data[offset + 13] = _settings.Height;
        }
        _overlayUpload!.WriteBytes(_overlayBytes);
        using var queue = _device!.Queue;
        queue.WriteBuffer(_overlayBuffer!, 0, _overlayUpload);
    }

    private void DrawOverlays(GPURenderPassEncoder pass, IReadOnlyList<ScreenOverlay> layers)
    {
        if (layers.Count == 0) return;
        pass.SetPipeline(_overlayPipeline!);
        for (var i = 0; i < layers.Count; i++)
        {
            pass.SetBindGroup(0, _overlayImages[layers[i].Texture].BindGroup);
            pass.Draw(6, 1, 0, (uint)i);
        }
    }

    private async Task<GPUTexture> LoadOverlayTextureAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var image = new HTMLImageElement { Src = path };
        try { await image.Decode(); }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            // Match missing world-image behavior without preventing scene startup.
            var fallback = _device!.CreateTexture(new GPUTextureDescriptor
            {
                Label = $"WolfNET Overlay Missing: {path}", Size = [1, 1, 1], Format = "rgba8unorm",
                Usage = GPUTextureUsage.TextureBinding | GPUTextureUsage.CopyDst
            });
            using var pixels = new Uint8Array(new byte[] { 255, 0, 255, 255 });
            using var fallbackQueue = _device.Queue;
            fallbackQueue.WriteTexture(new GPUTexelCopyTextureInfo { Texture = fallback }, pixels,
                new GPUTexelCopyBufferLayout { BytesPerRow = 4, RowsPerImage = 1 },
                new GPUExtent3DDict { Width = 1, Height = 1, DepthOrArrayLayers = 1 });
            return fallback;
        }
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var texture = _device!.CreateTexture(new GPUTextureDescriptor
        {
            Label = $"WolfNET Overlay Texture: {path}",
            Size = [image.NaturalWidth, image.NaturalHeight, 1], Format = "rgba8unorm",
            Usage = GPUTextureUsage.TextureBinding | GPUTextureUsage.CopyDst | GPUTextureUsage.RenderAttachment
        });
        try
        {
            using var window = _js.Get<Window>("window");
            using var bitmap = await window.CreateImageBitmap(image, new ImageBitmapOptions { PremultiplyAlpha = "premultiply" });
            try
            {
                token.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                using var queue = _device.Queue;
                queue.CopyExternalImageToTexture(new GPUCopyExternalImageSourceInfo { Source = bitmap },
                    new GPUCopyExternalImageDestInfo { Texture = texture, PremultipliedAlpha = true },
                    new GPUExtent3DDict { Width = (uint)image.NaturalWidth, Height = (uint)image.NaturalHeight, DepthOrArrayLayers = 1 });
            }
            finally { bitmap.Close(); }
            return texture;
        }
        catch
        {
            texture.Destroy();
            texture.Dispose();
            throw;
        }
    }

    private void ReleaseOverlays()
    {
        foreach (var image in _overlayImages)
        {
            image.BindGroup?.Dispose(); image.View.Dispose();
            image.Texture.Destroy(); image.Texture.Dispose();
        }
        _overlayImages.Clear();
        _overlaySampler?.Dispose(); _overlaySampler = null;
        DestroyBuffer(_overlayBuffer); _overlayBuffer = null;
        _overlayUpload?.Dispose(); _overlayUpload = null;
        _overlayBytes = []; _overlayCapacity = 0;
    }
}
