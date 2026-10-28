using System.Numerics;

namespace WolfNET.Engine.World;

public static class MapInteraction
{
    // Use the actor's facing direction. Trace through
    // open passages only, so walls, closed doors, and fences block interaction.
    public static string? FindTarget(IMap map, Vector2 position, Vector2 direction, float maxDistance = 1.75f)
    {
        if (!float.IsFinite(maxDistance) || maxDistance < 0)
            throw new ArgumentOutOfRangeException(nameof(maxDistance));
        if (!float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() <= 0) return null;
        direction = Vector2.Normalize(direction);
        var targets = map.RendererMap.Sprites.Where(sprite => sprite.InteractionTarget != null)
            .GroupBy(sprite => sprite.OwnerCell).ToDictionary(group => group.Key, group => group.First().InteractionTarget);
        if (targets.Count == 0) return null;
        var x = (int)MathF.Floor(position.X); var y = (int)MathF.Floor(position.Y);
        var stepX = direction.X < 0 ? -1 : 1; var stepY = direction.Y < 0 ? -1 : 1;
        var deltaX = direction.X == 0 ? float.PositiveInfinity : MathF.Abs(1 / direction.X);
        var deltaY = direction.Y == 0 ? float.PositiveInfinity : MathF.Abs(1 / direction.Y);
        var sideX = direction.X == 0 ? float.PositiveInfinity : (direction.X < 0 ? position.X - x : x + 1 - position.X) * deltaX;
        var sideY = direction.Y == 0 ? float.PositiveInfinity : (direction.Y < 0 ? position.Y - y : y + 1 - position.Y) * deltaY;
        var entered = 0f;
        while (entered <= maxDistance)
        {
            if (x < 0 || y < 0 || x >= map.RendererMap.Width || y >= map.RendererMap.Height) return null;
            if (targets.TryGetValue((uint)(y * map.RendererMap.Width + x + 1), out var destination)) return destination;
            if (!map.NavigationMap.IsWalkable(x, y)) return null;
            // At a diagonal corner both side cells must be open; do not reach
            // through two obstacles that touch at a corner.
            if (MathF.Abs(sideX - sideY) < .00001f &&
                (!map.NavigationMap.IsWalkable(x + stepX, y) || !map.NavigationMap.IsWalkable(x, y + stepY))) return null;
            if (sideX < sideY) { entered = sideX; sideX += deltaX; x += stepX; }
            else { entered = sideY; sideY += deltaY; y += stepY; }
        }
        return null;
    }
}
