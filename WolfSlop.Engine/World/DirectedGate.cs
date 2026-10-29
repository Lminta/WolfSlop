using System.Numerics;

namespace WolfSlop.Engine.World;

/// <summary>A finite directed trigger segment for checkpoints, portals, counters, or sensors.</summary>
public readonly record struct DirectedGate
{
    public Vector2 Center { get; }
    public Vector2 Direction { get; }
    public float HalfWidth { get; }

    public DirectedGate(Vector2 center, Vector2 direction, float halfWidth)
    {
        if (!float.IsFinite(center.X) || !float.IsFinite(center.Y) ||
            !float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() <= 0 ||
            !float.IsFinite(halfWidth) || halfWidth <= 0)
            throw new ArgumentException("A gate needs a finite center, nonzero direction and positive half-width.");
        Center = center; Direction = Vector2.Normalize(direction); HalfWidth = halfWidth;
    }

    /// <summary>Returns the forward crossing's fraction of the motion segment, or null.</summary>
    public float? Crossed(Vector2 from, Vector2 to)
    {
        var before = Vector2.Dot(from - Center, Direction);
        var after = Vector2.Dot(to - Center, Direction);
        if (before > 0 || after <= 0 || after <= before) return null;
        var fraction = -before / (after - before);
        var point = Vector2.Lerp(from, to, fraction) - Center;
        var across = new Vector2(-Direction.Y, Direction.X);
        return MathF.Abs(Vector2.Dot(point, across)) <= HalfWidth ? fraction : null;
    }
}
