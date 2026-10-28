using System.Numerics;
using WolfNET.Engine.Core;

namespace WolfNET.Engine.Actors.Components;

public class CameraComponent : ComponentBase, ICamera
{
    public CameraComponent(ObjectBase parent) : base(parent)
    {
    }

    public Transform Transform => Parent.Transform;
    public Vector2 Plane { get; set; } = new(0, 0.66f);
    // Radians; positive looks up. Simulated pitch shifts the projected horizon.
    public float Pitch { get; set; }
    public float Elevation { get; set; }
    public bool ThirdPerson { get; set; }
    public CameraView View(Graphics.RenderMap map) => ThirdPersonCamera.View(Transform, Pitch, Elevation, ThirdPerson, map);
}
