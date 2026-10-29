// Only device input is substituted. Objects, components, camera, and simulation use production code.
using System.Numerics;
using WolfSlop.Engine.Core;
using WolfSlop.Engine.Input;

sealed class TestObject : ObjectBase { }

sealed class FakeKeyboard : IKeyboardInput
{
    private KeyState _state;
    private bool _jumpRequested;
    public KeyState KeyState
    {
        get => _state;
        set
        {
            if (value.Jump && !_state.Jump) _jumpRequested = true;
            _state = value;
        }
    }
    public bool ViewToggleRequested { get; set; }
    public bool ActionRequested { get; set; }
    public bool ConsumeViewToggle() { var result = ViewToggleRequested; ViewToggleRequested = false; return result; }
    public bool ConsumeAction() { var result = ActionRequested; ActionRequested = false; return result; }
    public bool ConsumeJump() { var result = _jumpRequested; _jumpRequested = false; return result; }
}

sealed class FakeMouse : IMouseInput
{
    public float HorizontalMovement { get; set; }
    public float VerticalMovement { get; set; }
    public Vector2 ConsumeMovement()
    {
        var result = new Vector2(HorizontalMovement, VerticalMovement);
        HorizontalMovement = VerticalMovement = 0;
        return result;
    }
}
