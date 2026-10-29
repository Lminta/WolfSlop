using System.Text.Json.Serialization;

namespace WolfSlop.Engine.World;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PassageDefinition), "passage")]
[JsonDerivedType(typeof(WallDefinition), "wall")]
[JsonDerivedType(typeof(DoorDefinition), "door")]
[JsonDerivedType(typeof(SpriteDefinition), "sprite")]
[JsonDerivedType(typeof(SpriteGroupDefinition), "spriteGroup")]
public abstract record MapObjectDefinition
{
    public required int Id { get; init; }
    public required bool RaycastTransparent { get; init; }
    public required bool Collides { get; init; }
    public bool Jumpable { get; init; }
    public bool Crouchable { get; init; }
    public string ImpactMaterial { get; init; } = "hard";
    public string? AmbientSound { get; init; }
}

public sealed record PassageDefinition : MapObjectDefinition
{
    // Driving behavior is independent of the floor's artwork.
    public string Surface { get; init; } = "mud";
    public string? FloorTexture { get; init; }
    public string? CeilingTexture { get; init; }
    public string CeilingMode { get; init; } = "texture";
}

public sealed record WallDefinition : MapObjectDefinition
{
    public WallTextures? Textures { get; init; }
    public required int PassageId { get; init; }
}

public sealed record DoorDefinition : MapObjectDefinition
{
    public required int PassageId { get; init; }
    public string? Texture { get; init; }
    // Normal axis: x separates east/west, y separates north/south.
    public string Axis { get; init; } = "y";
    public float OpenSeconds { get; init; } = .8f;
    // Delay after fully opening; nonpositive values disable automatic closing.
    public float AutoCloseSeconds { get; init; }
}

public sealed record WallTextures
{
    public string? North { get; init; }
    public string? East { get; init; }
    public string? South { get; init; }
    public string? West { get; init; }
}

public sealed record AtlasFrame
{
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
}

public sealed record SpriteAtlas
{
    public required AtlasFrame[] Frames { get; init; }
    public int Directions { get; init; } = 1;
}

public sealed record SpriteAnimation
{
    public float FramesPerSecond { get; init; } = 4;
    public float PhaseSeconds { get; init; }
}

public sealed record SpriteDefinition : MapObjectDefinition
{
    // Physical horizontal dimensions for directional props, in blocks.
    public SpriteFootprint? Footprint { get; init; }
    // Optional application-defined semantic identifier.
    public string? Tag { get; init; }
    public string? InteractionTarget { get; init; }
    public string? Texture { get; init; }
    public SpriteAtlas? Atlas { get; init; }
    public SpriteAnimation? Animation { get; init; }
    public float RotationDegrees { get; init; }
    public bool FacingCamera { get; init; } = true;
    // Optional fixed quad normal, matching door axes; overrides FacingCamera.
    public string? PlaneAxis { get; init; }
    public required uint TextureLayer { get; init; }
    public required int PassageId { get; init; }
}

public sealed record SpriteFootprint
{
    public required float Length { get; init; }
    public required float Width { get; init; }
}

public sealed record SpriteGroupDefinition : MapObjectDefinition
{
    public required int PassageId { get; init; }
    public required SpriteGroupMember[] Sprites { get; init; }
}

public sealed record SpriteGroupMember
{
    public required int SpriteId { get; init; }
    public required MapOffset Offset { get; init; }
    public SpriteScale Scale { get; init; } = new();
}

public sealed record SpriteScale
{
    public float X { get; init; } = 1;
    public float Y { get; init; } = 1;
}

public sealed record MapOffset
{
    public required float X { get; init; }
    public required float Y { get; init; }
    public float Z { get; init; }
}
