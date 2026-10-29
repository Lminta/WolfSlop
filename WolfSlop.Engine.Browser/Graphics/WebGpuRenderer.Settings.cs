using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Engine.Graphics;

namespace WolfNET.Engine.Browser.Graphics;

public sealed partial class WebGpuRenderer
{
    public void ApplySettings(RendererSettings settings, HTMLCanvasElement canvas)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized) throw new InvalidOperationException("Initialize the renderer before changing settings.");
        settings.Validate();
        var resized = settings.Width != _settings.Width || settings.Height != _settings.Height;
        var filteringChanged = settings.MaxAnisotropy != _settings.MaxAnisotropy;
        _settings.Apply(settings);
        ReleaseColumnFrame();

        if (resized)
        {
            canvas.Width = _settings.Width;
            canvas.Height = _settings.Height;
            _computeBindGroup?.Dispose();
            DestroyBuffer(_rayHitsBuffer);
            _rayHitsBuffer = _device!.CreateBuffer(new GPUBufferDescriptor
            {
                Label = "WolfNET Ray Hits", Size = checked((ulong)_settings.Width * RayHitSize),
                Usage = GPUBufferUsage.Storage
            });
            using var layout = _computePipeline!.GetBindGroupLayout(0);
            _computeBindGroup = _device.CreateBindGroup(new GPUBindGroupDescriptor
            {
                Label = "WolfNET Compute Bindings", Layout = layout,
                Entries = [BufferEntry(0, _cameraBuffer!), BufferEntry(1, _worldMapBuffer!),
                    BufferEntry(2, _rayHitsBuffer), BufferEntry(3, _mapInfoBuffer!)]
            });
            _frameSubmission!.JSRef!.CallVoid("setComputeGroup", _computeBindGroup);
        }
        if (filteringChanged) _textures?.UpdateFiltering();
        if ((resized || filteringChanged) && _textures != null)
        {
            CreateWorldBindings();
            // Rebuild sprite/minimap bindings before the next submission, retaining scene textures.
            _spriteCapacity = 0;
            _browserSprites!.ClearSubmission();
        }
    }

    private void CreateWorldBindings()
    {
        _renderBindGroup?.Dispose();
        using var layout = _renderPipeline!.GetBindGroupLayout(0);
        _renderBindGroup = _device!.CreateBindGroup(new GPUBindGroupDescriptor
        {
            Label = "WolfNET Render Bindings", Layout = layout,
            Entries =
            [
                BufferEntry(0, _rayHitsBuffer!), BufferEntry(1, _cameraBuffer!),
                new GPUBindGroupEntry { Binding = 2, Resource = _textures!.WallView },
                BufferEntry(3, _worldMapBuffer!), BufferEntry(4, _mapInfoBuffer!),
                BufferEntry(6, _materialsBuffer!),
                new GPUBindGroupEntry { Binding = 7, Resource = _textures.TextureSampler },
                new GPUBindGroupEntry { Binding = 8, Resource = _textures.SurfaceSampler },
                new GPUBindGroupEntry { Binding = 9, Resource = _textures.SkyView },
                new GPUBindGroupEntry { Binding = 10, Resource = _textures.SkySampler },
                BufferEntry(11, _textures.SkyBuffer)
            ]
        });
    }
}
