namespace WolfSlop.Engine.Input;

/// <summary>Maps application action names to physical key identifiers. Multiple keys may share an action.</summary>
public sealed class InputBindings
{
    private readonly Dictionary<string, string[]> _actions;

    public InputBindings(IReadOnlyDictionary<string, string[]> actions)
    {
        if (actions.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null ||
            pair.Value.Length == 0 || pair.Value.Any(string.IsNullOrWhiteSpace)))
            throw new ArgumentException("Input actions need a name and at least one key code.", nameof(actions));
        _actions = actions.ToDictionary(pair => pair.Key, pair => (string[])pair.Value.Clone(), StringComparer.Ordinal);
    }

    public bool Handles(string code) => _actions.Values.Any(keys => keys.Contains(code, StringComparer.Ordinal));
    internal bool Held(string action, HashSet<string> held) =>
        _actions.TryGetValue(action, out var keys) && keys.Any(held.Contains);
    internal IEnumerable<string> ActionsFor(string code) =>
        _actions.Where(pair => pair.Value.Contains(code, StringComparer.Ordinal)).Select(pair => pair.Key);

    public static InputBindings Default { get; } = new(new Dictionary<string, string[]>
    {
        ["forward"] = ["KeyW"], ["backward"] = ["KeyS"],
        ["strafeLeft"] = ["KeyA"], ["strafeRight"] = ["KeyD"],
        ["left"] = ["ArrowLeft"], ["right"] = ["ArrowRight"],
        ["jump"] = ["Space"], ["crouch"] = ["AltLeft", "AltRight"],
        ["view"] = ["KeyV"], ["interact"] = ["KeyF"]
    });
}
