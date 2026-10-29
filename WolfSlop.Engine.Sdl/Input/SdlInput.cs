using System.Numerics;
using SDL3;
using WolfSlop.Engine.Input;

namespace WolfSlop.Engine.Sdl.Input;

public sealed class SdlInput(nint window, bool useRelativeMouse = true) : IKeyboardInput, IMouseInput
{
    public KeyboardState Keyboard { get; } = new();
    private readonly KeyboardState _touchKeyboard = new();
    private readonly Dictionary<ulong, string> _touches = [];
    private Vector2 _movement;
    public bool Captured { get; private set; }
    public KeyState KeyState
    {
        get
        {
            var state = Keyboard.KeyState; var touch = _touchKeyboard.KeyState;
            state.Forward |= touch.Forward; state.Backward |= touch.Backward;
            state.StrafeLeft |= touch.StrafeLeft; state.StrafeRight |= touch.StrafeRight; state.Jump |= touch.Jump;
            return state;
        }
    }
    public bool ConsumeJump() => Keyboard.ConsumeJump();
    public bool ConsumeViewToggle() => Keyboard.ConsumeViewToggle() | _touchKeyboard.ConsumeViewToggle();
    public bool ConsumeAction() => Keyboard.ConsumeAction();
    public Vector2 ConsumeMovement() { var movement = _movement; _movement = default; return movement; }
    public void Capture()
    {
        if (useRelativeMouse && !OperatingSystem.IsAndroid() && !SDL.SetWindowRelativeMouseMode(window, true)) throw new InvalidOperationException(SDL.GetError());
        Captured = true; Keyboard.Reset(); _movement = default;
    }
    public void Release()
    {
        if (useRelativeMouse && !OperatingSystem.IsAndroid()) SDL.SetWindowRelativeMouseMode(window, false);
        Captured = false; Keyboard.Reset(); _touchKeyboard.Reset(); _touches.Clear(); _movement = default;
    }
    public void Touch(SDL.TouchFingerEvent touch, bool released)
    {
        if (!Captured) return;
        var previous = _touches.Values.ToHashSet();
        if (released) _touches.Remove(touch.FingerID);
        else _touches[touch.FingerID] = touch.Y < .35f ? "KeyV" : touch.Y < .65f ? "" :
            touch.X < .25f ? "KeyA" : touch.X < .5f ? "KeyD" : touch.X < .75f ? "KeyS" : "KeyW";
        var current = _touches.Values.ToHashSet();
        foreach (var key in previous.Except(current)) _touchKeyboard.Release(key);
        foreach (var key in current.Except(previous)) _touchKeyboard.Press(key);
    }
    public static string Code(SDL.Scancode key) => key switch {
        SDL.Scancode.W => "KeyW", SDL.Scancode.S => "KeyS", SDL.Scancode.A => "KeyA", SDL.Scancode.D => "KeyD",
        SDL.Scancode.V => "KeyV", SDL.Scancode.F => "KeyF", SDL.Scancode.Space => "Space", SDL.Scancode.Left => "ArrowLeft",
        SDL.Scancode.Right => "ArrowRight", SDL.Scancode.LAlt => "AltLeft", SDL.Scancode.RAlt => "AltRight", _ => "" };
    public void Handle(SDL.Event e)
    {
        if (e.Type == (uint)SDL.EventType.WindowFocusLost) Release();
        if (!Captured) return;
        if (e.Type == (uint)SDL.EventType.KeyDown) Keyboard.Press(Code(e.Key.Scancode), e.Key.Repeat);
        if (e.Type == (uint)SDL.EventType.KeyUp) Keyboard.Release(Code(e.Key.Scancode));
        if (e.Type == (uint)SDL.EventType.MouseMotion) _movement += new Vector2(e.Motion.XRel, e.Motion.YRel);
    }
}
