using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using SDL3;

namespace WolfSlop.Engine.Sdl.Graphics;

public sealed unsafe class GpuDevice : IDisposable
{
    public nint Handle { get; }
    public nint Window { get; }
    public static nint Require(nint handle) => handle != 0 ? handle : throw new InvalidOperationException(SDL.GetError());
    public static void Check(bool success) { if (!success) throw new InvalidOperationException(SDL.GetError()); }

    public GpuDevice(nint window, bool debug = false, string? driver = null)
    {
        Window = window;
        Handle = Require(SDL.CreateGPUDevice(SDL.GPUShaderFormat.SPIRV | SDL.GPUShaderFormat.DXIL, debug, driver));
        if (!SDL.ClaimWindowForGPUDevice(Handle, window)) { SDL.DestroyGPUDevice(Handle); throw new InvalidOperationException(SDL.GetError()); }
        if (!SDL.SetGPUSwapchainParameters(Handle, window, SDL.GPUSwapchainComposition.SDR, SDL.GPUPresentMode.Immediate))
            Check(SDL.SetGPUSwapchainParameters(Handle, window, SDL.GPUSwapchainComposition.SDR, SDL.GPUPresentMode.VSync));
        Console.WriteLine($"[GPU] SDL {SDL.GetGPUDeviceDriver(Handle)}");
    }

    public nint Shader(string file, string entry, SDL.GPUShaderStage stage, uint samplers, uint buffers, uint uniforms)
    {
        var code = Compile(file, entry, out var size, out var format);
        var entryBytes = Encoding.UTF8.GetBytes(entry + '\0');
        try
        {
            fixed (byte* pointer = entryBytes)
                return Require(SDL.CreateGPUShader(Handle, new SDL.GPUShaderCreateInfo {
                    Code = code, CodeSize = size, _entrypoint = (nint)pointer, Format = format,
                    Stage = stage, NumSamplers = samplers, NumStorageBuffers = buffers, NumUniformBuffers = uniforms }));
        }
        finally { Marshal.FreeHGlobal(code); }
    }

    public nint Compute(string file)
    {
        var code = Compile(file, "main", out var size, out var format);
        var entry = Encoding.UTF8.GetBytes("main\0");
        try
        {
            fixed (byte* pointer = entry)
                return Require(SDL.CreateGPUComputePipeline(Handle, new SDL.GPUComputePipelineCreateInfo {
                    Code = code, CodeSize = size, Entrypoint = (nint)pointer, Format = format,
                    NumReadonlyStorageBuffers = 1, NumReadwriteStorageBuffers = 1, NumUniformBuffers = 2,
                    ThreadcountX = 64, ThreadcountY = 1, ThreadcountZ = 1 }));
        }
        finally { Marshal.FreeHGlobal(code); }
    }

    private nint Compile(string file, string entry, out nuint size, out SDL.GPUShaderFormat format)
    {
        format = (SDL.GetGPUShaderFormats(Handle) & SDL.GPUShaderFormat.DXIL) != 0 ? SDL.GPUShaderFormat.DXIL : SDL.GPUShaderFormat.SPIRV;
        var extension = format == SDL.GPUShaderFormat.DXIL ? "dxil" : "spv";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"WolfSlop.Engine.Sdl.Shaders.Compiled.{file}.{extension}")
            ?? throw new FileNotFoundException($"Precompiled shader {file}.{extension} is missing. Run Tools/ShaderCompiler.");
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        var data = bytes.ToArray(); size = (nuint)data.Length;
        var pointer = Marshal.AllocHGlobal(data.Length); Marshal.Copy(data, 0, pointer, data.Length); return pointer;
    }

    public nint Pipeline(nint vertex, nint fragment, SDL.GPUTextureFormat format, bool blend = false)
    {
        var target = new SDL.GPUColorTargetDescription { Format = format, BlendState = new() {
            EnableBlend = blend, SrcColorBlendFactor = SDL.GPUBlendFactor.One,
            DstColorBlendFactor = SDL.GPUBlendFactor.OneMinusSrcAlpha, ColorBlendOp = SDL.GPUBlendOp.Add,
            SrcAlphaBlendFactor = SDL.GPUBlendFactor.One, DstAlphaBlendFactor = SDL.GPUBlendFactor.OneMinusSrcAlpha, AlphaBlendOp = SDL.GPUBlendOp.Add } };
        return Require(SDL.CreateGPUGraphicsPipeline(Handle, new SDL.GPUGraphicsPipelineCreateInfo {
            VertexShader = vertex, FragmentShader = fragment, PrimitiveType = SDL.GPUPrimitiveType.TriangleList,
            RasterizerState = new() { CullMode = SDL.GPUCullMode.None, FillMode = SDL.GPUFillMode.Fill, EnableDepthClip = true },
            TargetInfo = new() { ColorTargetDescriptions = (nint)(&target), NumColorTargets = 1 } }));
    }

    public nint Buffer(uint size, SDL.GPUBufferUsageFlags usage) => Require(SDL.CreateGPUBuffer(Handle, new() { Size = Math.Max(16, size), Usage = usage }));
    public nint Texture(uint width, uint height, uint layers = 1, uint levels = 1, bool target = false) =>
        Require(SDL.CreateGPUTexture(Handle, new() { Type = layers > 1 ? SDL.GPUTextureType.TextureType2DArray : SDL.GPUTextureType.TextureType2D,
            Format = SDL.GPUTextureFormat.R8G8B8A8Unorm, Width = width, Height = height, LayerCountOrDepth = layers, NumLevels = levels,
            Usage = SDL.GPUTextureUsageFlags.Sampler | (target ? SDL.GPUTextureUsageFlags.ColorTarget : 0) }));

    public void Upload(nint command, nint destination, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0) return;
        var transfer = Transfer(bytes);
        var pass = SDL.BeginGPUCopyPass(command);
        SDL.UploadToGPUBuffer(pass, new SDL.GPUTransferBufferLocation { TransferBuffer = transfer },
            new SDL.GPUBufferRegion { Buffer = destination, Size = (uint)bytes.Length }, true);
        SDL.EndGPUCopyPass(pass);
        SDL.ReleaseGPUTransferBuffer(Handle, transfer);
    }

    public void UploadTexture(nint command, nint texture, nint pixels, uint width, uint height, uint layer = 0, uint mip = 0)
    {
        var transfer = Transfer(new ReadOnlySpan<byte>((void*)pixels, checked((int)(width * height * 4))));
        var pass = SDL.BeginGPUCopyPass(command);
        SDL.UploadToGPUTexture(pass, new SDL.GPUTextureTransferInfo { TransferBuffer = transfer, PixelsPerRow = width, RowsPerLayer = height },
            new SDL.GPUTextureRegion { Texture = texture, W = width, H = height, D = 1, Layer = layer, MipLevel = mip }, false);
        SDL.EndGPUCopyPass(pass);
        SDL.ReleaseGPUTransferBuffer(Handle, transfer);
    }

    private nint Transfer(ReadOnlySpan<byte> bytes)
    {
        var transfer = Require(SDL.CreateGPUTransferBuffer(Handle, new() { Usage = SDL.GPUTransferBufferUsage.Upload, Size = (uint)bytes.Length }));
        var mapped = Require(SDL.MapGPUTransferBuffer(Handle, transfer, false));
        bytes.CopyTo(new Span<byte>((void*)mapped, bytes.Length));
        SDL.UnmapGPUTransferBuffer(Handle, transfer);
        return transfer;
    }
    public void Dispose() { SDL.WaitForGPUIdle(Handle); SDL.ReleaseWindowFromGPUDevice(Handle, Window); SDL.DestroyGPUDevice(Handle); }
}
