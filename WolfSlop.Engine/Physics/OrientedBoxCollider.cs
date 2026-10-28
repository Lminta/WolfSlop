using System.Numerics;

namespace WolfNET.Engine.Physics;

public readonly record struct BoxImpact(int CellX, int CellY, Vector2 Position, float Speed);
public readonly record struct BoxMotion(Vector2 Position, Vector2 Direction, Vector2 Velocity, bool Collided)
{
    public BoxImpact? Impact { get; init; }
}

/// <summary>Sweeps a car-sized oriented rectangle through the navigation grid.</summary>
public sealed class OrientedBoxCollider(INavigationMap map, float length, float width)
{
    private const float Clearance = .00002f;
    private readonly float _halfLength = length * .5f;
    private readonly float _halfWidth = width * .5f;

    public bool OverlapsCell(Vector2 position, Vector2 direction, int x, int y)
    {
        direction = Vector2.Normalize(direction);
        var right = new Vector2(-direction.Y, direction.X);
        var separation = position - new Vector2(x + .5f, y + .5f);
        var extent = Extent(direction);
        if (extent.X + .5f - MathF.Abs(separation.X) <= 0 ||
            extent.Y + .5f - MathF.Abs(separation.Y) <= 0) return false;
        // The same four SAT axes as Contact, without calculating a correction
        // normal and penetration depth for surface and footprint queries.
        var axisLengthSquared = Vector2.Dot(direction, direction);
        var cellRadius = .5f * (MathF.Abs(direction.X) + MathF.Abs(direction.Y));
        return _halfLength * axisLengthSquared + cellRadius - MathF.Abs(Vector2.Dot(separation, direction)) > 0 &&
               _halfWidth * axisLengthSquared + cellRadius - MathF.Abs(Vector2.Dot(separation, right)) > 0;
    }

    public bool CanOccupy(Vector2 position, Vector2 direction)
    {
        direction = Vector2.Normalize(direction);
        var extent = Extent(direction);
        for (var x = (int)MathF.Floor(position.X - extent.X); x <= (int)MathF.Floor(position.X + extent.X); x++)
        for (var y = (int)MathF.Floor(position.Y - extent.Y); y <= (int)MathF.Floor(position.Y + extent.Y); y++)
            if (!map.IsWalkable(x, y) && Contact(position, direction, x, y, out _, out _)) return false;
        return true;
    }

    public BoxMotion MoveAndSlide(Vector2 position, Vector2 direction, Vector2 movement, float rotation,
        Vector2 velocity)
    {
        direction = Vector2.Normalize(direction);
        var cornerRadius = MathF.Sqrt(_halfLength * _halfLength + _halfWidth * _halfWidth);
        var steps = Math.Max(1, Math.Max(
            (int)MathF.Ceiling((movement.Length() + cornerRadius * MathF.Abs(rotation)) / .05f),
            (int)MathF.Ceiling(MathF.Abs(rotation) / (MathF.PI / 90))));
        var origin = position;
        var correction = Vector2.Zero;
        var angle = rotation / steps;
        var collided = false;
        BoxImpact? impact = null;
        for (var step = 0; step < steps; step++)
        {
            var nextDirection = Rotate(direction, angle);
            // Derive each point from the original sweep. Repeatedly adding tiny
            // steps loses visible precision at the far end of a large map.
            var desired = origin + movement * ((step + 1) / (float)steps);
            var candidate = desired + correction;
            var nextVelocity = velocity;
            var touched = ResolveContacts(ref candidate, nextDirection, ref nextVelocity, ref impact);
            if (CanOccupy(candidate, nextDirection))
            {
                position = candidate;
                direction = nextDirection;
                velocity = nextVelocity;
            }
            else
            {
                // A tightly wedged car can keep its previous valid orientation.
                // Do not force a rotation through opposing walls.
                candidate = desired + correction;
                nextVelocity = velocity;
                ResolveContacts(ref candidate, direction, ref nextVelocity, ref impact);
                if (CanOccupy(candidate, direction))
                {
                    position = candidate;
                    velocity = nextVelocity;
                }
                else velocity = Vector2.Zero;
                touched = true;
            }
            correction = position - desired;
            collided |= touched;
        }
        return new(position, direction, velocity, collided) { Impact = impact };
    }

    private bool ResolveContacts(ref Vector2 position, Vector2 direction, ref Vector2 velocity, ref BoxImpact? impact)
    {
        var touched = false;
        for (var iteration = 0; iteration < 12; iteration++)
        {
            var corrected = false;
            var extent = Extent(direction);
            var minX = (int)MathF.Floor(position.X - extent.X);
            var maxX = (int)MathF.Floor(position.X + extent.X);
            var minY = (int)MathF.Floor(position.Y - extent.Y);
            var maxY = (int)MathF.Floor(position.Y + extent.Y);
            for (var x = minX; x <= maxX; x++)
            for (var y = minY; y <= maxY; y++)
            {
                if (map.IsWalkable(x, y) || !Contact(position, direction, x, y, out var normal, out var depth)) continue;
                position += normal * (depth + Clearance);
                var inward = Vector2.Dot(velocity, normal);
                if (-inward > (impact?.Speed ?? 0)) impact = new(x, y, new(x + .5f, y + .5f), -inward);
                if (inward < 0) velocity -= normal * inward;
                corrected = touched = true;
            }
            if (!corrected) break;
        }
        return touched;
    }

    private Vector2 Extent(Vector2 direction) => new(
        _halfLength * MathF.Abs(direction.X) + _halfWidth * MathF.Abs(direction.Y),
        _halfLength * MathF.Abs(direction.Y) + _halfWidth * MathF.Abs(direction.X));

    private bool Contact(Vector2 position, Vector2 direction, int x, int y, out Vector2 normal, out float depth)
    {
        var right = new Vector2(-direction.Y, direction.X);
        var separation = position - new Vector2(x + .5f, y + .5f);
        normal = Vector2.Zero;
        depth = float.PositiveInfinity;
        // Separating-axis theorem: the two axes of each rectangle are sufficient.
        Span<Vector2> axes = stackalloc Vector2[] { Vector2.UnitX, Vector2.UnitY, direction, right };
        foreach (var axis in axes)
        {
            var signedDistance = Vector2.Dot(separation, axis);
            var carRadius = _halfLength * MathF.Abs(Vector2.Dot(direction, axis)) +
                            _halfWidth * MathF.Abs(Vector2.Dot(right, axis));
            var cellRadius = .5f * (MathF.Abs(axis.X) + MathF.Abs(axis.Y));
            var overlap = carRadius + cellRadius - MathF.Abs(signedDistance);
            if (overlap <= 0) return false;
            if (overlap >= depth) continue;
            depth = overlap;
            normal = signedDistance >= 0 ? axis : -axis;
        }
        return true;
    }

    private static Vector2 Rotate(Vector2 direction, float angle)
    {
        var cosine = MathF.Cos(angle);
        var sine = MathF.Sin(angle);
        return new(direction.X * cosine - direction.Y * sine, direction.X * sine + direction.Y * cosine);
    }
}
