using WolfSlop.Engine.Actors.Components;
using WolfSlop.Engine.Graphics;
using System.Numerics;

namespace WolfSlop.Engine.Actors;

public interface IControlledActor
{
    Transform Transform { get; set; }
    CameraComponent Camera { get; }
    bool InAir { get; }
    bool Crouching { get; }
    int AnimationFrame { get; }
    float InteractionRadius { get; }
}

