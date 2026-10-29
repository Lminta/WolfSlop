using System.Numerics;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Engine.World;
using WolfNET.Engine.Actors;

using WolfNET.Engine.Graphics;

namespace WolfNET.Engine.Browser.Graphics;

public sealed partial class WebGpuRenderer
{
    public async Task InitializeAsync(
        HTMLCanvasElement canvas,
        ShaderSources shaders,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var (computeShaderSource, screenShaderSource, spriteShaderSource, overlayShaderSource) = shaders;

        if (_initialized)
        {
            throw new InvalidOperationException(
                "Renderer is already initialized.");
        }

        _settings.Validate();

        Console.WriteLine("[GPU] Initialization started");

        try
        {
            if (!_js.Get<bool>("window.isSecureContext"))
            {
                throw new NotSupportedException(
                    "WebGPU requires a secure connection. Open this game over HTTPS " +
                    "with a certificate trusted by this device. HTTP only works on localhost; " +
                    "an HTTP LAN IP address cannot use WebGPU. See README.md for LAN HTTPS setup.");
            }

            // 1. Request GPU adapter and device.

            using var navigator =
                _js.Get<Navigator>("navigator");

            using var gpu = navigator.Gpu
                ?? throw new NotSupportedException(
                    "WebGPU is unavailable in this browser. Use a WebGPU-capable browser " +
                    "with hardware acceleration enabled and a supported graphics driver.");

            Console.WriteLine("[GPU] Requesting adapter");

            using var adapter = await gpu.RequestAdapter(new GPURequestAdapterOptions { PowerPreference = "high-performance" })
                ?? await gpu.RequestAdapter()
                ?? throw new Exception(
                    "No WebGPU adapter available.");

            using (var info = adapter.Info)
                Console.WriteLine($"[GPU] Adapter: {info.Vendor} {info.Architecture} {info.Device} {info.Description}");

            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);

            _device = await adapter.RequestDevice()
                ?? throw new Exception(
                    "Failed to create WebGPU device.");

            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);

            _device.OnUncapturedError += OnGpuError;
            _profiling = _js.Get<string>("location.search").TrimStart('?').Split('&').Contains("profile=1");
            _profileSampleStart = System.Diagnostics.Stopwatch.GetTimestamp();

            Console.WriteLine("[GPU] Device created");

            // 2. Configure canvas.

            _context = canvas.GetWebGPUContext()
                ?? throw new Exception(
                    "WebGPU canvas context is unavailable.");

            var format = gpu.GetPreferredCanvasFormat();
            _canvasFormat = format;

            _context.Configure(
                new GPUCanvasConfiguration
                {
                    Device = _device,
                    Format = format,
                    Usage = (uint)(GPUTextureUsage.RenderAttachment | GPUTextureUsage.CopySrc)
                });

            Console.WriteLine(
                $"[GPU] Canvas configured: {format}");

            // 3. Allocate persistent GPU buffers.

            CreateBuffers();

            using var spriteModule = await _js.Import<JSObject>("./_content/WolfNET.Engine.Browser/sprite-sort.js");
            if (cancellationToken.IsCancellationRequested || _disposed)
            {
                spriteModule.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
            }
            using (var queue = _device.Queue)
                _browserSprites = new BrowserSpriteSelection(spriteModule.JSRef!.Call<JSObject>("createSelector"), queue, _cameraBuffer!);


            // 4. Compile shaders.

            Console.WriteLine("[GPU] Creating shader modules");

            using var computeShader =
                _device.CreateShaderModule(
                    new GPUShaderModuleDescriptor
                    {
                        Label = "WolfNET DDA Shader",
                        Code = computeShaderSource
                    });

            using var screenShader =
                _device.CreateShaderModule(
                    new GPUShaderModuleDescriptor
                    {
                        Label = "WolfNET Screen Shader",
                        Code = screenShaderSource
                    });

            // 5. Create compute pipeline.

            Console.WriteLine("[GPU] Creating compute pipeline");

            _device.PushErrorScope(
                GPUErrorFilter.Validation);

            _computePipeline =
                _device.CreateComputePipeline(
                    new GPUComputePipelineDescriptor
                    {
                        Label = "WolfNET Raycast Pipeline",
                        Layout = GPUAutoLayoutMode.Auto,

                        Compute = new GPUProgrammableStage
                        {
                            Module = computeShader,
                            EntryPoint = "main"
                        }
                    });

            using var computeError =
                await _device.PopErrorScope();

            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (computeError != null)
            {
                throw new Exception(
                    $"Compute pipeline validation failed: " +
                    computeError.Message);
            }

            // 6. Create render pipeline.

            Console.WriteLine("[GPU] Creating render pipeline");

            _device.PushErrorScope(
                GPUErrorFilter.Validation);

            _renderPipeline =
                _device.CreateRenderPipeline(
                    new GPURenderPipelineDescriptor
                    {
                        Label = "WolfNET Screen Pipeline",
                        Layout = GPUAutoLayoutMode.Auto,

                        Vertex = new GPUVertexState
                        {
                            Module = screenShader,
                            EntryPoint = "vertexMain"
                        },

                        Fragment = new GPUFragmentState
                        {
                            Module = screenShader,
                            EntryPoint = "fragmentMain",

                            Targets =
                            [
                                new GPUColorTargetState
                                {
                                    Format = format
                                }
                            ]
                        }
                    });

            using var renderError =
                await _device.PopErrorScope();

            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (renderError != null)
            {
                throw new Exception(
                    $"Render pipeline validation failed: " +
                    renderError.Message);
            }

            await CreatePresentPipelineAsync(screenShader, format, cancellationToken);

            // Reuse the screen shader module and all existing scene buffers.
            _device.PushErrorScope(GPUErrorFilter.Validation);
            _minimapPipeline = _device.CreateRenderPipeline(new GPURenderPipelineDescriptor
            {
                Label = "WolfNET Minimap Pipeline",
                Layout = GPUAutoLayoutMode.Auto,
                Vertex = new GPUVertexState { Module = screenShader, EntryPoint = "vertexMain" },
                Fragment = new GPUFragmentState
                {
                    Module = screenShader,
                    EntryPoint = "minimapMain",
                    Targets = [new GPUColorTargetState { Format = format }]
                }
            });

            using var minimapError = await _device.PopErrorScope();
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (minimapError != null)
                throw new Exception($"Minimap pipeline validation failed: {minimapError.Message}");

            using var spriteShader = _device.CreateShaderModule(new GPUShaderModuleDescriptor
            {
                Label = "WolfNET Sprite Shader",
                Code = spriteShaderSource
            });

            _device.PushErrorScope(GPUErrorFilter.Validation);
            _spritePipeline = _device.CreateRenderPipeline(new GPURenderPipelineDescriptor
            {
                Label = "WolfNET Sprite Pipeline",
                Layout = GPUAutoLayoutMode.Auto,
                Vertex = new GPUVertexState { Module = spriteShader, EntryPoint = "vertexMain" },
                Fragment = new GPUFragmentState
                {
                    Module = spriteShader,
                    EntryPoint = "fragmentMain",
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

            using var spriteError = await _device.PopErrorScope();
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (spriteError != null)
            {
                throw new Exception($"Sprite pipeline validation failed: {spriteError.Message}");
            }

            await CreateOverlayPipelineAsync(overlayShaderSource, format, cancellationToken);

            // 7. Create compute bind group.

            Console.WriteLine("[GPU] Creating compute bind group");

            using (var layout =
                   _computePipeline.GetBindGroupLayout(0))
            {
                _computeBindGroup =
                    _device.CreateBindGroup(
                        new GPUBindGroupDescriptor
                        {
                            Label = "WolfNET Compute Bindings",
                            Layout = layout,

                            Entries =
                            [
                                BufferEntry(0, _cameraBuffer!),
                                BufferEntry(1, _worldMapBuffer!),
                                BufferEntry(2, _rayHitsBuffer!),
                                BufferEntry(3, _mapInfoBuffer!)
                            ]
                        });
            }

            using var submissionModule = await _js.Import<JSObject>("./_content/WolfNET.Engine.Browser/frame-submission.js");
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            _frameSubmission = submissionModule.JSRef!.CallApply<JSObject>("createSubmission",
                new object[] { _device, _context, _computePipeline, _computeBindGroup, _renderPipeline, _spritePipeline, _presentPipeline! });

            _uploadedMap = null;
            _frameCount = 0;
            _initialized = true;

            Console.WriteLine(
                "[GPU] Initialization completed successfully");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[GPU] Initialization failed: {ex}");

            Dispose();
            throw;
        }
    }

}
