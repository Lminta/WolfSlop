using System.Text.Json;

namespace WolfSlop.Engine.Assets;

/// <summary>Application-owned content; paths use forward slashes relative to the content root.</summary>
public interface IAssetSource
{
    Stream Open(string path);
}

public static class AssetSourceExtensions
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true
    };

    public static async Task<T> ReadJsonAsync<T>(this IAssetSource assets, string path,
        CancellationToken cancellationToken = default)
    {
        using var stream = assets.Open(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException($"Asset '{path}' is empty.");
    }

    public static string RelativeTo(string parent, string path)
    {
        if (Path.IsPathRooted(path) || path.Contains(':')) throw new InvalidDataException("Asset paths must be relative.");
        return (Path.GetDirectoryName(parent.Replace('/', Path.DirectorySeparatorChar)) is { Length: > 0 } dir
            ? Path.Combine(dir, path) : path).Replace('\\', '/');
    }
}

public sealed class DirectoryAssetSource(string root) : IAssetSource
{
    private readonly string _root = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
    public string Resolve(string path)
    {
        var resolved = Path.GetFullPath(Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(_root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException($"Asset '{path}' escapes the content directory.");
        return resolved;
    }
    public Stream Open(string path) => File.OpenRead(Resolve(path));
}
