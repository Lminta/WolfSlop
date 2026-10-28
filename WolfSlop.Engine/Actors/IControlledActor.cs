using WolfNET.Engine.Actors.Components;
using WolfNET.Engine.Graphics;
using System.Numerics;

namespace WolfNET.Engine.Actors;

public interface IControlledActor
{
    Transform Transform { get; set; }
    CameraComponent Camera { get; }
    bool InAir { get; }
    bool Crouching { get; }
    int AnimationFrame { get; }
    float InteractionRadius { get; }
}

