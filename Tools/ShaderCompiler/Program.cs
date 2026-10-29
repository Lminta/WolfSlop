using System.Text;
using SDL3;
using System.Security.Cryptography;
using System.Text.Json;

var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root != null && !Directory.Exists(Path.Combine(root.FullName, "WolfSlop.Engine.Sdl", "Shaders"))) root = root.Parent;
if (root == null) throw new DirectoryNotFoundException("Cannot find engine shader sources.");
var sourceRoot = Path.Combine(root.FullName, "WolfSlop.Engine.Sdl", "Shaders");
var outputRoot = Path.Combine(sourceRoot, "Compiled"); Directory.CreateDirectory(outputRoot);
if (!ShaderCross.Init()) throw new Exception(SDL.GetError());
try
{
    foreach (var path in Directory.GetFiles(sourceRoot, "*.hlsl"))
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var entry = name == "sprites-vertex" ? "vertexMain" : name == "sprites-fragment" ? "fragmentMain" : "main";
        var stage = name == "raycast" ? ShaderCross.ShaderStage.Compute : name is "fullscreen" or "sprites-vertex" ? ShaderCross.ShaderStage.Vertex : ShaderCross.ShaderStage.Fragment;
        Compile(path, entry, stage, Path.Combine(outputRoot, name));
    }
    var hashes = Directory.GetFiles(sourceRoot, "*.hlsl").Order().ToDictionary(Path.GetFileName,
        path => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(path).Replace("\r\n", "\n")))));
    File.WriteAllText(Path.Combine(outputRoot, "sources.json"), JsonSerializer.Serialize(hashes, new JsonSerializerOptions { WriteIndented = true }));
}
finally { ShaderCross.Quit(); }

static unsafe void Compile(string path, string entry, ShaderCross.ShaderStage stage, string output)
{
    var source = Encoding.UTF8.GetBytes(File.ReadAllText(path) + '\0');
    var entryBytes = Encoding.UTF8.GetBytes(entry + '\0');
    fixed (byte* text = source)
    fixed (byte* name = entryBytes)
    {
        var info = new ShaderCross.HLSLInfo { Source = (nint)text, Entrypoint = (nint)name, ShaderStage = stage };
        foreach (var format in new[] { "spv", "dxil" })
        {
            var code = format == "spv" ? ShaderCross.CompileSPIRVFromHLSL(ref info, out var size) : ShaderCross.CompileDXILFromHLSL(info, out size);
            if (code == 0) throw new Exception($"{path}: {SDL.GetError()}");
            try { File.WriteAllBytes(output + "." + format, new ReadOnlySpan<byte>((void*)code, checked((int)size)).ToArray()); }
            finally { SDL.Free(code); }
        }
        Console.WriteLine($"Compiled {Path.GetFileName(path)} → SPIR-V + DXIL");
    }
}
