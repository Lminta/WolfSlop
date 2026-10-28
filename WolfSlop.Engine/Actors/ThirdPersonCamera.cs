using System.Numerics;
using WolfNET.Engine.Graphics;
using WolfNET.Engine.Physics;

namespace WolfNET.Engine.Actors;

public readonly record struct CameraView(Vector2 Position, float Elevation, float Distance);

public static class ThirdPersonCamera
{
    public const float FollowDistance = 1.5f;
    public const float ShoulderOffset = 0f;
    private const float Radius = .1f;

    public static CameraView View(Transform player, float pitch, float elevation, bool enabled, RenderMap map)
    {
        if (!enabled) return new(player.Position, elevation, 0);
        var direction = Vector2.Normalize(player.Rotation);
        var desired = FollowDistance * MathF.Cos(pitch);
        var right = new Vector2(direction.Y, -direction.X);
        var boom = -direction * desired + right * ShoulderOffset;
        var length = boom.Length();
        float fraction = 0;
        // Sweep the complete shoulder offset as well as the backward movement.
        for (float next = .025f; next <= length + .025f; next += .025f)
        {
            var step = MathF.Min(next / length, 1);
            if (!Clear(player.Position + boom * step, map)) break;
            fraction = step;
            if (step == 1) break;
        }
        var distance = desired * fraction;
        var height = Math.Clamp(elevation + .25f - MathF.Tan(pitch) * distance, -.45f, .45f);
        return new(player.Position + boom * fraction, height, distance);
    }

    private static bool Clear(Vector2 point, RenderMap map)
    {
        for (int x = (int)MathF.Floor(point.X - Radius); x <= (int)MathF.Floor(point.X + Radius); x++)
        for (int y = (int)MathF.Floor(point.Y - Radius); y <= (int)MathF.Floor(point.Y + Radius); y++)
        {
            if (x < 0 || y < 0 || x >= map.Width || y >= map.Height) return false;
            var cell = map.Cells[y * map.Width + x];
            if ((cell.Flags & (RenderCellFlags.Wall | RenderCellFlags.StopsRay)) == 0) continue;
            if (CircleGeometry.OverlapsCell(point, Radius, x, y)) return false;
        }
        return true;
    }
}
