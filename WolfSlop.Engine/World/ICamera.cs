using System.Numerics;

namespace WolfSlop.Engine.World;

public interface ICamera
{
    Transform Transform { get; }
    Vector2 Plane { get; }
    float Pitch { get; }
    float Elevation { get; }
}
