using System.Numerics;

namespace WolfSlop.Engine.Input;

public interface IMouseInput
{
    Vector2 ConsumeMovement();
}
