using System.Collections;
using System.Numerics;

namespace WolfSlop.Engine.UI;

/// <summary>A retained UI node. A node has one parent; a document owns its attached tree.</summary>
public class UiElement
{
    private UiStyle _style = new();
    private bool _visible = true, _enabled = true, _focusable, _blocksInput;
    private string? _label, _role;
    private int _tabIndex;
    public string Id { get; }
    public UiElement? Parent { get; internal set; }
    public UiDocument? Document { get; internal set; }
    public UiChildren Children { get; }
    public UiRect Bounds { get; internal set; }
    public Matrix3x2 WorldTransform { get; internal set; } = Matrix3x2.Identity;
    public bool Visible { get => _visible; set => Set(ref _visible, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public bool Focusable { get => _focusable; set => Set(ref _focusable, value); }
    public bool BlocksInput { get => _blocksInput; set => Set(ref _blocksInput, value); }
    public int TabIndex { get => _tabIndex; set => Set(ref _tabIndex, value); }
    public string? Label { get => _label; set => Set(ref _label, value); }
    public string? Role { get => _role; set => Set(ref _role, value); }
    public UiStyle Style { get => _style; set => Set(ref _style, value ?? throw new ArgumentNullException(nameof(value))); }
    /// <summary>Updates the visual rotation without repeating measurement and arrangement.</summary>
    public float Rotation
    {
        get => _style.Rotation;
        set
        {
            if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (_style.Rotation == value) return;
            _style = _style with { Rotation = value };
            Document?.InvalidateVisual();
        }
    }
    public bool IsFocused => ReferenceEquals(Document?.Input.Focused, this);
    public bool IsHovered => ReferenceEquals(Document?.Input.Hovered, this);
    public bool IsPressed => ReferenceEquals(Document?.Input.Pressed, this);
    internal virtual string Kind => "panel";
    internal virtual bool Interactive => BlocksInput || Focusable || PointerDown != null || Clicked != null;
    public event Action<UiPointerEvent>? PointerDown;
    public event Action<UiPointerEvent>? PointerUp;
    public event Action<UiPointerEvent>? PointerMove;
    public event Action<UiPointerEvent>? Clicked;
    public event Action<bool>? FocusChanged;

    public UiElement(string? id = null)
    {
        Id = id ?? Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("UI IDs must be nonempty.", nameof(id));
        Children = new(this);
    }

    protected void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value; Invalidate();
    }
    protected void SetVisual<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value; Document?.InvalidateVisual();
    }
    public void Invalidate() => Document?.Invalidate();
    public T Add<T>(T child) where T : UiElement { Children.Add(child); return child; }
    public IEnumerable<UiElement> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var descendant in child.DescendantsAndSelf()) yield return descendant;
    }
    internal void RaiseFocus(bool focused) => FocusChanged?.Invoke(focused);
    internal virtual void Activate(UiPointerEvent e) => Clicked?.Invoke(e);
    internal void Dispatch(string kind, UiPointerEvent e)
    {
        switch (kind)
        {
            case "down": PointerDown?.Invoke(e); break;
            case "up": PointerUp?.Invoke(e); break;
            case "move": PointerMove?.Invoke(e); break;
            case "click": Activate(e); break;
        }
    }
    internal virtual Vector2 Measure(Vector2 available, IUiTextMeasurer metrics, UiTheme theme) => Vector2.Zero;
}

public sealed class UiPanel(string? id = null) : UiElement(id);

public sealed class UiChildren(UiElement owner) : IReadOnlyList<UiElement>
{
    private readonly List<UiElement> _items = [];
    public int Count => _items.Count;
    public UiElement this[int index] => _items[index];
    public void Add(UiElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Parent != null || child.Document != null) throw new InvalidOperationException("Detach a UI node before reparenting it.");
        if (child.DescendantsAndSelf().Any(node => ReferenceEquals(node, owner))) throw new InvalidOperationException("UI trees cannot contain cycles.");
        owner.Document?.Attach(child);
        child.Parent = owner;
        _items.Add(child); owner.Invalidate();
    }
    public bool Remove(UiElement child)
    {
        if (!_items.Remove(child)) return false;
        owner.Document?.Detach(child);
        child.Parent = null; owner.Invalidate();
        return true;
    }
    public void Clear() { foreach (var child in _items.ToArray()) Remove(child); }
    public IEnumerator<UiElement> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
