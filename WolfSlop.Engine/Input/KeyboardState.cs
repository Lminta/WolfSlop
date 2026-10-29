namespace WolfSlop.Engine.Input;

/// <summary>Held keys and pending actions, independent of host event lifetimes.</summary>
public sealed class KeyboardState : IKeyboardInput
{
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    public InputBindings Bindings { get; }
    public KeyboardState(InputBindings? bindings = null) => Bindings = bindings ?? InputBindings.Default;
    public bool IsHeld(string action) => Bindings.Held(action, _held);
    public bool ConsumePressed(string action) => _pending.Remove(action);

    public KeyState KeyState => new()
    {
        Forward = IsHeld("forward"),
        Backward = IsHeld("backward"),
        StrafeLeft = IsHeld("strafeLeft"),
        StrafeRight = IsHeld("strafeRight"),
        Left = IsHeld("left"),
        Right = IsHeld("right"),
        Jump = IsHeld("jump"),
        Crouch = IsHeld("crouch")
    };

    public static bool Handles(string code) => InputBindings.Default.Handles(code);

    public void Press(string code, bool repeat = false)
    {
        // A held key must not restart movement after capture loss clears input.
        if (repeat || !Bindings.Handles(code) || !_held.Add(code)) return;
        foreach (var action in Bindings.ActionsFor(code))
        {
            if (action == "view" && _pending.Remove(action)) continue;
            _pending.Add(action);
        }
    }

    public void Release(string code) => _held.Remove(code);

    public bool ConsumeJump() => ConsumePressed("jump");
    public bool ConsumeViewToggle() => ConsumePressed("view");
    public bool ConsumeAction() => ConsumePressed("interact");

    public void Reset()
    {
        _held.Clear();
        _pending.Clear();
    }

}
