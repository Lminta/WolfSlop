using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Engine.Graphics;

namespace WolfNET.Engine.Browser.Graphics;

public sealed partial class WebGpuRenderer
{
    private GPURenderPipeline? _presentPipeline;
    private string _canvasFormat = null!;
    private GPUTexture? _columnFrame;
    private GPUTextureView? _columnFrameView;
    private GPUTexture? _columnStrip;
    private GPUTextureView? _columnStripView;
    private GPUBindGroup? _presentBindGroup;
    private bool _columnFrameValid;
    private int _columnFactor;
    private int _columnPhase;

    private async Task CreatePresentPipelineAsync(GPUShaderModule shader, string format, CancellationToken token)
    {
        _device!.PushErrorScope(GPUErrorFilter.Validation);
        _presentPipeline = _device.CreateRenderPipeline(new GPURenderPipelineDescriptor
        {
            Label = "WolfNET Interlaced Presentation Pipeline",
            Layout = GPUAutoLayoutMode.Auto,
            Vertex = new GPUVertexState { Module = shader, EntryPoint = "vertexMain" },
            Fragment = new GPUFragmentState
            {
                Module = shader, EntryPoint = "presentMain",
                Targets = [new GPUColorTargetState { Format = format }]
            }
        });
        using var error = await _device.PopErrorScope();
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (error != null) throw new InvalidOperationException($"Presentation pipeline validation failed: {error.Message}");
    }

    private (int Step, int Phase) PrepareColumnFrame()
    {
        var factor = _settings.ColumnInterlaceFactor;
        if (factor is < 1 or > 3)
            throw new ArgumentException("Graphics columnInterlaceFactor must be 1, 2, or 3.");
        if (_columnFactor != factor)
        {
            ReleaseColumnFrame();
            _columnFactor = factor;
        }
        if (factor == 1)
        {
            if (_columnFrame != null) ReleaseColumnFrame();
            return (1, 0);
        }
        if (_columnFrame == null)
        {
            _columnFrame = _device!.CreateTexture(new GPUTextureDescriptor
            {
                Label = "WolfNET Interlaced Frame",
                Size = [_settings.Width, _settings.Height, 1],
                Format = _canvasFormat,
                Usage = GPUTextureUsage.CopyDst | GPUTextureUsage.TextureBinding
            });
            _columnFrameView = _columnFrame.CreateView();
            _columnStrip = _device.CreateTexture(new GPUTextureDescriptor
            {
                Label = "WolfNET Interlaced Columns",
                Size = [(_settings.Width + factor - 1) / factor, _settings.Height, 1],
                Format = _canvasFormat,
                Usage = GPUTextureUsage.RenderAttachment | GPUTextureUsage.TextureBinding
            });
            _columnStripView = _columnStrip.CreateView();
            using var layout = _presentPipeline!.GetBindGroupLayout(0);
            _presentBindGroup = _device.CreateBindGroup(new GPUBindGroupDescriptor
            {
                Label = "WolfNET Interlaced Presentation Bindings", Layout = layout,
                Entries = [BufferEntry(1, _cameraBuffer!),
                    new GPUBindGroupEntry { Binding = 12, Resource = _columnFrameView },
                    new GPUBindGroupEntry { Binding = 13, Resource = _columnStripView }]
            });
            _frameSubmission!.JSRef!.CallVoid("setColumns", _columnFrame, _columnStripView, _presentBindGroup);
        }
        return _columnFrameValid ? (factor, _columnPhase) : (1, 0);
    }

    private void DrawScreenLayers(GPURenderPassEncoder pass, RenderFrame frame)
    {
        DrawOverlays(pass, frame.Overlays);
        if (!frame.ShowMinimap) return;
        pass.SetPipeline(_minimapPipeline!);
        pass.SetBindGroup(0, _minimapBindGroup!);
        pass.Draw(3);
    }

    private void ReleaseColumnFrame()
    {
        _frameSubmission?.JSRef!.CallVoid("clearColumns");
        _presentBindGroup?.Dispose();
        _presentBindGroup = null;
        _columnFrameView?.Dispose();
        _columnFrameView = null;
        _columnFrame?.Destroy();
        _columnFrame?.Dispose();
        _columnFrame = null;
        _columnStripView?.Dispose();
        _columnStripView = null;
        _columnStrip?.Destroy();
        _columnStrip?.Dispose();
        _columnStrip = null;
        _columnFrameValid = false;
        _columnPhase = 0;
    }
}
