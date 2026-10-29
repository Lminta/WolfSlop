using System.Numerics;

namespace WolfSlop.Engine.Physics;

/// <summary>Sweeps a circle through a navigation grid and resolves sliding contacts.</summary>
public sealed class CircleCollider(INavigationMap map, float radius)
{
    private readonly INavigationMap _map = map;
    private float CollisionRadius => radius;
    private const float ContactClearance = 0.00002f;

    public Vector2 MoveAndSlide(Vector2 position, Vector2 movement, bool crouching = false)
    {
        // Short steps prevent crossing walls during fast movement or slow frames.
        var steps = Math.Max(1, (int)MathF.Ceiling(movement.Length() / (CollisionRadius * 0.5f)));
        var step = movement / steps;
        position = ResolveContacts(position, crouching);
        for (int i = 0; i < steps; i++)
        {
            var candidate = ResolveContacts(position + step, crouching);
            if (CanOccupy(candidate, crouching: crouching)) position = candidate;
        }
        return position;
    }

    public Vector2 Trace(Vector2 origin, Vector2 desired, bool inAir = false, bool crouching = false,
        IReadOnlySet<(int X, int Y)>? blockers = null)
    {
        var delta = desired - origin;
        var steps = Math.Max(1, (int)MathF.Ceiling(delta.Length() / (CollisionRadius * 0.5f)));
        var previous = origin;
        for (int i = 1; i <= steps; i++)
        {
            var candidate = origin + delta * (i / (float)steps);
            if (CanOccupy(candidate, inAir, crouching, blockers)) { previous = candidate; continue; }
            float clear = 0, blocked = 1;
            for (int iteration = 0; iteration < 16; iteration++)
            {
                var fraction = (clear + blocked) * 0.5f;
                var point = Vector2.Lerp(previous, candidate, fraction);
                if (CanOccupy(point, inAir, crouching, blockers)) clear = fraction;
                else blocked = fraction;
            }
            var distance = MathF.Max(0, Vector2.Distance(previous, candidate) * clear - ContactClearance);
            return previous + Vector2.Normalize(delta) * distance;
        }
        return desired;
    }

    public Vector2 ResolveContacts(Vector2 position, bool crouching = false)
    {
        // Push along the contact normal, preserving tangential movement around corners.
        // A small clearance prevents rounding errors from pinning the player to a wall.
        for (int iteration = 0; iteration < 8; iteration++)
        {
            var corrected = false;
            var minX = (int)MathF.Floor(position.X - CollisionRadius);
            var maxX = (int)MathF.Floor(position.X + CollisionRadius);
            var minY = (int)MathF.Floor(position.Y - CollisionRadius);
            var maxY = (int)MathF.Floor(position.Y + CollisionRadius);
            for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                if (_map.IsWalkable(x, y, crouching: crouching)) continue;
                var nearest = Vector2.Clamp(position, new(x, y), new(x + 1, y + 1));
                var separation = position - nearest;
                var distanceSquared = separation.LengthSquared();
                if (distanceSquared >= CollisionRadius * CollisionRadius) continue;
                if (distanceSquared > 0)
                {
                    var distance = MathF.Sqrt(distanceSquared);
                    position += separation / distance * (CollisionRadius + ContactClearance - distance);
                }
                else
                {
                    // Recover a spawn or edited scene whose circle starts inside a cell.
                    var left = position.X - x;
                    var right = x + 1 - position.X;
                    var top = position.Y - y;
                    var bottom = y + 1 - position.Y;
                    var closest = MathF.Min(MathF.Min(left, right), MathF.Min(top, bottom));
                    if (closest == left) position.X = x - CollisionRadius - ContactClearance;
                    else if (closest == right) position.X = x + 1 + CollisionRadius + ContactClearance;
                    else if (closest == top) position.Y = y - CollisionRadius - ContactClearance;
                    else position.Y = y + 1 + CollisionRadius + ContactClearance;
                }
                corrected = true;
            }
            if (!corrected) break;
        }
        return position;
    }

    public bool CanOccupy(Vector2 position, bool inAir = false, bool crouching = false,
        IReadOnlySet<(int X, int Y)>? blockers = null)
    {
        for (int x = (int)MathF.Floor(position.X - CollisionRadius); x <= (int)MathF.Floor(position.X + CollisionRadius); x++)
        for (int y = (int)MathF.Floor(position.Y - CollisionRadius); y <= (int)MathF.Floor(position.Y + CollisionRadius); y++)
        {
            if (_map.IsWalkable(x, y, inAir, crouching) && (blockers == null || !blockers.Contains((x, y)))) continue;
            if (CircleGeometry.OverlapsCell(position, CollisionRadius, x, y)) return false;
        }
        return true;
    }
}
