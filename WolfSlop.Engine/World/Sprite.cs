using System.Numerics;

namespace WolfSlop.Engine.World;

[Flags]
public enum SpriteKind : uint
{
    Billboard = 0,
    PlaneNormalX = 1,
    PlaneNormalY = 2,
    DoorNormalX = PlaneNormalX,
    DoorNormalY = PlaneNormalY,
    Opaque = 4,
    FixedPlane = 8,
    DirectionalPlane = 16
}

// OwnerCell is the flattened cell index plus one, matching RayHit.hitCell.
public readonly record struct Sprite(Vector2 Position, uint TextureLayer, uint OwnerCell)
{
    public string? InteractionTarget { get; init; }
    public string? Tag { get; init; }
    public bool SwapRedGreen { get; init; }
    public bool SwapRedBlue { get; init; }
    public SpriteKind Kind { get; init; }
    public Vector2 Scale { get; init; } = Vector2.One;
    public SpriteFootprint? Footprint { get; init; }
    public float Elevation { get; init; }
    public SpriteAtlas? Atlas { get; init; }
    public SpriteAnimation? Animation { get; init; }
    public float RotationDegrees { get; init; }
    public bool FacingCamera { get; init; } = true;

    // Animated atlases use absolute scene seconds; ordinary atlases use the view direction.
    // World X is east, world Y is south; positive angles turn clockwise.
    public int ViewFrame(Vector2 observer, double elapsedSeconds = 0)
    {
        if (Animation is { } animation && Atlas is { } animatedAtlas)
        {
            if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            var cycleSeconds = animatedAtlas.Frames.Length / (double)animation.FramesPerSecond;
            var cyclePosition = (elapsedSeconds % cycleSeconds + animation.PhaseSeconds % cycleSeconds) % cycleSeconds;
            return Math.Min(animatedAtlas.Frames.Length - 1, (int)(cyclePosition * animation.FramesPerSecond));
        }
        if (Atlas is not { Directions: > 1 } atlas) return 0;
        var relative = observer - Position;
        if (relative.LengthSquared() < 0.00000001f) return 0;
        var turns = (MathF.Atan2(relative.Y, relative.X) - RotationDegrees * MathF.PI / 180) / MathF.Tau;
        var frame = (int)MathF.Floor(turns * atlas.Directions + 0.5f);
        return (frame % atlas.Directions + atlas.Directions) % atlas.Directions;
    }

    public uint ViewLayer(Vector2 observer, double elapsedSeconds = 0) => TextureLayer + (uint)ViewFrame(observer, elapsedSeconds);

    // A non-billboard atlas holds its quad normal at the selected view's center.
    // The geometry word keeps flags in its low 16 bits and a world-space normal
    // angle (one turn = 65536) in its high 16 bits, preserving the GPU record size.
    public uint ViewKind(Vector2 observer)
        => ViewKind(ViewFrame(observer));

    /// <summary>Geometry flags and packed normal for an already selected atlas frame.</summary>
    public uint ViewKind(int frame)
    {
        if (FacingCamera || Atlas is not { Directions: > 1 } atlas || Kind != SpriteKind.Billboard)
            return (uint)Kind | (SwapRedBlue ? 32u : 0u) | (SwapRedGreen ? 64u : 0u);
        var degrees = (RotationDegrees % 360 + frame * (360f / atlas.Directions)) % 360;
        if (degrees < 0) degrees += 360;
        var angle = (uint)MathF.Round(degrees / 360 * 65536) & 0xffffu;
        return (uint)(SpriteKind.FixedPlane | SpriteKind.DirectionalPlane) | (angle << 16) | (SwapRedBlue ? 32u : 0u) | (SwapRedGreen ? 64u : 0u);
    }

    public Vector2 ViewScale(Vector2 observer, double elapsedSeconds = 0)
    {
        return new(ViewWidth(ViewFrame(observer, elapsedSeconds)), Scale.Y);
    }

    /// <summary>World-space quad width for an already selected atlas frame.</summary>
    public float ViewWidth(int frameIndex)
    {
        if (Footprint is { } footprint && Atlas is { Directions: > 1 } directional)
        {
            var angle = frameIndex * MathF.Tau / directional.Directions;
            return Scale.X * (footprint.Width * MathF.Abs(MathF.Cos(angle)) +
                footprint.Length * MathF.Abs(MathF.Sin(angle)));
        }
        return Scale.X * (Atlas is { } atlas ? (float)atlas.Frames[frameIndex].Width / atlas.Frames[frameIndex].Height : 1);
    }

    /// <summary>Conservative width across atlas frames for provider-side visibility selection.</summary>
    public float MaximumViewWidth => Atlas is { } atlas
        ? Enumerable.Range(0, atlas.Frames.Length).Max(ViewWidth) : Scale.X;
}
