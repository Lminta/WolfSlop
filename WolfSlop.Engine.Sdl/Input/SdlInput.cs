using System.Numerics;
using SDL3;
using WolfSlop.Engine.Input;

namespace WolfSlop.Engine.Sdl.Input;

public sealed class SdlInput(nint window, bool useRelativeMouse = true) : IKeyboardInput, IMouseInput
{
    public KeyboardState Keyboard { get; } = new();
    private Vector2 _movement;
    public bool Captured { get; private set; }
    public KeyState KeyState => Keyboard.KeyState;
    public bool ConsumeJump() => Keyboard.ConsumeJump();
    public bool ConsumeViewToggle() => Keyboard.ConsumeViewToggle();
    public bool ConsumeAction() => Keyboard.ConsumeAction();
    public Vector2 ConsumeMovement() { var movement = _movement; _movement = default; return movement; }
    public void Capture()
    {
        if (useRelativeMouse && !SDL.SetWindowRelativeMouseMode(window, true)) throw new InvalidOperationException(SDL.GetError());
        Captured = true; Keyboard.Reset(); _movement = default;
    }
    public void Release()
    {
        if (useRelativeMouse) SDL.SetWindowRelativeMouseMode(window, false);
        Captured = false; Keyboard.Reset(); _movement = default;
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
