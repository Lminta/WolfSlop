using WolfNET.Engine.World;

namespace WolfNET.Engine.Graphics;

[Flags]
public enum RenderCellFlags : uint
{
    None = 0,
    StopsRay = 1,
    Wall = 2,
    SkyCeiling = 4,
    Door = 8,
    DoorNormalX = 16
}

// All GPU records contain four 32-bit values, matching the WGSL structures.
public readonly record struct RenderCell(
    uint MaterialIndex, uint FloorTextureLayer, uint CeilingTextureLayer, RenderCellFlags Flags);

public readonly record struct WallMaterial(uint North, uint East, uint South, uint West);

public sealed class TextureCatalog
{
    public const uint MissingLayer = uint.MaxValue;
    public const int TextureSize = 64;
    public const uint MaxLayers = 256;
    public IReadOnlyDictionary<uint, string?> Layers { get; }
    public uint LayerCount { get; }
    public IReadOnlyDictionary<uint, AtlasFrame> Frames { get; }

    public TextureCatalog(Dictionary<uint, string?> layers, Dictionary<uint, AtlasFrame>? frames = null)
    {
        if (layers.Keys.Any(layer => layer >= MaxLayers) || frames?.Keys.Any(layer => !layers.ContainsKey(layer)) == true)
            throw new ArgumentException("Texture slots must fit within the catalog; frames need a matching texture layer.");
        Layers = new System.Collections.ObjectModel.ReadOnlyDictionary<uint, string?>(new Dictionary<uint, string?>(layers));
        Frames = new System.Collections.ObjectModel.ReadOnlyDictionary<uint, AtlasFrame>(new Dictionary<uint, AtlasFrame>(frames ?? []));
        LayerCount = layers.Count == 0 ? 1 : checked(layers.Keys.Max() + 1);
    }
}

public sealed record RenderMap(
    int Width,
    int Height,
    IReadOnlyList<RenderCell> Cells,
    IReadOnlyList<WallMaterial> Materials,
    IReadOnlyList<Sprite> Sprites,
    TextureCatalog Textures,
    ulong Revision)
{
    public SkyDefinition? Sky { get; init; }
    public IReadOnlyDictionary<string, System.Text.Json.JsonElement> Metadata { get; init; } = new Dictionary<string, System.Text.Json.JsonElement>();
}
