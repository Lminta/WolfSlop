using System.Numerics;
using WolfSlop.Engine.World;

namespace WolfSlop.Engine.Graphics;

/// <summary>A camera snapshot and any number of application-owned moving sprites and screen layers.</summary>
public readonly record struct RenderFrame(
    Vector2 Position, Vector2 Direction, Vector2 Plane, RenderMap Map,
    float Pitch = 0, float Elevation = 0)
{
    public IReadOnlyList<Sprite> DynamicSprites { get; init; } = [];
    public IReadOnlyList<ScreenOverlay> Overlays { get; init; } = [];
    public bool ShowMinimap { get; init; } = true;
    public bool CullSpritesOutsideView { get; init; }
}

/// <summary>Normalized screen coordinates. Position anchors Pivot; rotation is in radians.
/// UV is (left, top, width, height). Layers are composited in list order.</summary>
public readonly record struct ScreenOverlay(int Texture, Vector2 Position, Vector2 Size,
    Vector2 Pivot = default, float Rotation = 0)
{
    public Vector4 UV { get; init; } = new(0, 0, 1, 1);
}

public sealed record SceneResources(TextureCatalog? Textures = null, IReadOnlyList<string>? OverlayTextures = null);
