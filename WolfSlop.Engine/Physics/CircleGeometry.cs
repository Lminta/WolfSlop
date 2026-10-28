using System.Numerics;

namespace WolfNET.Engine.Physics;

public static class CircleGeometry
{
    public static bool OverlapsCell(Vector2 center, float radius, int x, int y) =>
        Vector2.DistanceSquared(center, Vector2.Clamp(center, new(x, y), new(x + 1, y + 1))) < radius * radius;
}
