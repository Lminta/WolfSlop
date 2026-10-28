using System.Numerics;

namespace WolfNET.Engine.UI;

public sealed class UiPointerEvent(UiElement target, Vector2 position, int pointerId = 0)
{
    public UiElement Target { get; } = target;
    public Vector2 Position { get; } = position;
    public int PointerId { get; } = pointerId;
    public bool Handled { get; set; }
}

public sealed class UiInputRouter(UiDocument document)
{
    private readonly Dictionary<int, UiElement> _captured = [];
    public UiElement? Focused { get; private set; }
    public UiElement? Hovered { get; private set; }
    public UiElement? Pressed => _captured.Values.FirstOrDefault();
    public event Action<string>? KeyPressed;

    public bool Focus(UiElement? element)
    {
        if (element != null && (!element.Focusable || !document.Available(element) ||
            document.ModalRoot is { } modal && !UiDocument.IsWithin(element, modal))) return false;
        if (ReferenceEquals(Focused, element)) return true;
        var previous = Focused; Focused = element;
        if (element != null)
            for (var parent = element.Parent; parent != null; parent = parent.Parent)
                if (parent is UiScrollView scroll && Matrix3x2.Invert(scroll.WorldTransform, out var inverse))
                {
                    var point = Vector2.Transform(Vector2.Transform(Vector2.Zero, element.WorldTransform), inverse);
                    if (point.Y < scroll.Style.Padding.Top) scroll.Offset += point.Y - scroll.Style.Padding.Top;
                    else if (point.Y + element.Bounds.Height > scroll.Bounds.Height - scroll.Style.Padding.Bottom)
                        scroll.Offset += point.Y + element.Bounds.Height - scroll.Bounds.Height + scroll.Style.Padding.Bottom;
                }
        previous?.RaiseFocus(false); element?.RaiseFocus(true); document.Invalidate();
        return true;
    }

    public bool MoveFocus(bool backwards = false)
    {
        var candidates = document.Root.DescendantsAndSelf().Where(node => node.Focusable && node.TabIndex >= 0 && document.Available(node) &&
            (document.ModalRoot == null || UiDocument.IsWithin(node, document.ModalRoot))).OrderBy(node => node.TabIndex == 0 ? int.MaxValue : node.TabIndex).ToArray();
        if (candidates.Length == 0) return false;
        var index = Array.IndexOf(candidates, Focused);
        if (index < 0) return Focus(candidates[backwards ? ^1 : 0]);
        return Focus(candidates[(index + (backwards ? -1 : 1) + candidates.Length) % candidates.Length]);
    }

    public bool PointerDown(Vector2 position, int pointerId = 0)
    {
        var target = document.HitTest(position);
        if (target == null) { Focus(null); return false; }
        _captured[pointerId] = target;
        if (target.Focusable) Focus(target);
        if (target is UiSlider slider) slider.SetFromPointer(position);
        Bubble(target, "down", position, pointerId); document.Invalidate();
        return true;
    }
    public bool PointerMove(Vector2 position, int pointerId = 0)
    {
        var target = document.HitTest(position);
        if (!ReferenceEquals(target, Hovered)) { Hovered = target; document.Invalidate(); }
        target = _captured.GetValueOrDefault(pointerId) ?? target;
        if (target == null) return false;
        if (target is UiSlider slider && _captured.ContainsKey(pointerId) && document.Available(slider)) slider.SetFromPointer(position);
        Bubble(target, "move", position, pointerId); return true;
    }
    public bool PointerUp(Vector2 position, int pointerId = 0)
    {
        if (!_captured.Remove(pointerId, out var target)) return false;
        if (document.Available(target))
        {
            Bubble(target, "up", position, pointerId);
            if (ReferenceEquals(document.HitTest(position), target)) Bubble(target, "click", position, pointerId);
        }
        document.Invalidate(); return true;
    }
    public bool Activate(string id)
    {
        var target = document.Find(id);
        if (target == null || !document.Available(target) || document.ModalRoot is { } modal && !UiDocument.IsWithin(target, modal)) return false;
        if (target.Focusable) Focus(target);
        Bubble(target, "click", default, 0); return true;
    }
    public bool Wheel(Vector2 position, float delta)
    {
        for (var node = document.HitTest(position); node != null; node = node.Parent)
            if (node is UiScrollView scroll && scroll.Extent > 0)
            { scroll.Offset += delta; return true; }
        return false;
    }
    public bool KeyDown(string key, bool shift = false, bool repeat = false)
    {
        if (key == "Tab") return MoveFocus(shift);
        if (Focused is not { } target || !document.Available(target)) return false;
        if (target is UiTextInput editor)
        {
            if (key == "Enter" && !repeat) { editor.Submit(); return true; }
            return false;
        }
        if (target is UiSlider slider && key is "ArrowLeft" or "ArrowRight" or "Home" or "End")
        {
            slider.Value = key switch { "Home" => 0, "End" => 1, "ArrowLeft" => slider.Value - slider.Step, _ => slider.Value + slider.Step }; return true;
        }
        if (!repeat && key is "Enter" or " ") { Activate(target.Id); return true; }
        KeyPressed?.Invoke(key); return false;
    }
    public bool TextInput(string id, string value)
    {
        if (document.Find(id) is not UiTextInput editor || editor.ReadOnly || !document.Available(editor)) return false;
        var length = Math.Min(value.Length, editor.MaxLength);
        if (length > 0 && length < value.Length && char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length])) length--;
        editor.Text = value[..length]; return true;
    }
    public void Cancel()
    {
        if (_captured.Count == 0 && Hovered == null) return;
        _captured.Clear(); Hovered = null; document.Invalidate();
    }
    internal void Detach(IEnumerable<UiElement> nodes)
    {
        var removed = nodes.ToHashSet();
        var changed = false;
        foreach (var pointer in _captured.Where(pair => removed.Contains(pair.Value)).Select(pair => pair.Key).ToArray())
        { _captured.Remove(pointer); changed = true; }
        if (Hovered != null && removed.Contains(Hovered)) { Hovered = null; changed = true; }
        if (changed) document.Invalidate();
    }
    private static void Bubble(UiElement target, string kind, Vector2 position, int pointerId)
    {
        var e = new UiPointerEvent(target, position, pointerId);
        for (var node = target; node != null && !e.Handled; node = node.Parent) node.Dispatch(kind, e);
    }
}
