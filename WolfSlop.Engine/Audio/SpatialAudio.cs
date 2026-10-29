using System.Numerics;

namespace WolfSlop.Engine.Audio;

public static class SpatialAudio
{
    /// <summary>Distance attenuation and stereo pan in the camera's horizontal plane.</summary>
    public static (float Gain, float Pan) Mix(Vector2 listener, Vector2 forward, Vector2 source,
        float referenceDistance, float maxDistance)
    {
        var offset = source - listener;
        var distance = offset.Length();
        if (distance >= maxDistance) return (0, 0);
        var direction = forward.LengthSquared() > .00001f ? Vector2.Normalize(forward) : Vector2.UnitY;
        var pan = distance > .00001f ? Vector2.Dot(offset / distance, new(direction.Y, -direction.X)) : 0;
        var tail = Math.Clamp((maxDistance - distance) / (maxDistance - referenceDistance), 0, 1);
        return (MathF.Min(1, referenceDistance / MathF.Max(referenceDistance, distance)) * tail,
            Math.Clamp(pan, -1, 1));
    }
}
