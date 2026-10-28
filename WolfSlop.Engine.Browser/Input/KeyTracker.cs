using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

using WolfNET.Engine.Input;

namespace WolfNET.Engine.Browser.Input;

/// <summary>Adapts browser keyboard events to the engine's synchronous input state.</summary>
public sealed class KeyTracker : IKeyboardInput, IDisposable
{
    private readonly Window _window;
    private readonly Func<bool> _isActive;
    private readonly KeyboardState _state;
    private bool _disposed;

    public event Action? EscapePressed;
    public KeyState KeyState => Active ? _state.KeyState : default;
    private bool Active => !_disposed && _isActive();

    public KeyTracker(BlazorJSRuntime js, Func<bool> isActive, InputBindings? bindings = null)
    {
        _isActive = isActive;
        _state = new(bindings);
        _window = js.Get<Window>("window");
        _window.OnKeyDown += OnKeyDown;
        _window.OnKeyUp += OnKeyUp;
        _window.OnBlur += OnBlur;
    }

    private void OnKeyDown(KeyboardEvent e)
    {
        if (!Active) return;
        if (e.Code == "Escape")
        {
            Reset();
            EscapePressed?.Invoke();
            return;
        }
        if (!_state.Bindings.Handles(e.Code)) return;
        e.PreventDefault();
        _state.Press(e.Code, e.Repeat);
    }

    private void OnKeyUp(KeyboardEvent e)
    {
        if (Active && _state.Bindings.Handles(e.Code)) e.PreventDefault();
        _state.Release(e.Code);
    }

    public bool IsHeld(string action) => Active && _state.IsHeld(action);
    public bool ConsumePressed(string action) => _state.ConsumePressed(action) && Active;

    public void Reset() => _state.Reset();
    public bool ConsumeJump() => _state.ConsumeJump() && Active;
    public bool ConsumeViewToggle() => _state.ConsumeViewToggle() && Active;
    public bool ConsumeAction() => _state.ConsumeAction() && Active;
    private void OnBlur(Event e) => Reset();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.OnKeyDown -= OnKeyDown;
        _window.OnKeyUp -= OnKeyUp;
        _window.OnBlur -= OnBlur;
        Reset();
        EscapePressed = null;
        _window.Dispose();
    }
}
