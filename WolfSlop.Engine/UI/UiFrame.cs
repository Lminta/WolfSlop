namespace WolfSlop.Engine.UI;

public interface IUiRenderer : IDisposable
{
    void Render(UiFrame frame);
}

public sealed record UiClip(UiRect Rect, UiMatrix Transform, float Radius = 0);

/// <summary>Backend-independent, ordered UI display list entry.</summary>
public sealed record UiRenderNode
{
    public required string Id { get; init; }
    public string? ParentId { get; init; }
    public required string Kind { get; init; }
    public required float Width { get; init; }
    public required float Height { get; init; }
    public required UiMatrix Transform { get; init; }
    public IReadOnlyList<UiClip> Clips { get; init; } = [];
    public float Opacity { get; init; } = 1;
    public string? Color { get; init; }
    public string? Background { get; init; }
    public string? BorderColor { get; init; }
    public float BorderWidth { get; init; }
    public float CornerRadius { get; init; }
    public string? TextOutlineColor { get; init; }
    public float TextOutlineWidth { get; init; }
    public UiThickness Padding { get; init; }
    public string? Content { get; init; }
    public UiFont? Font { get; init; }
    public UiTextLayout? TextLayout { get; init; }
    public UiAlignment TextAlignment { get; init; }
    public string? Source { get; init; }
    public UiRect SourceRect { get; init; } = new(0, 0, 1, 1);
    public UiImageFit ImageFit { get; init; }
    public UiDrawing? Drawing { get; init; }
    public UiMatrix DrawingTransform { get; init; } = UiMatrix.Identity;
    public UiRect ViewBox { get; init; }
    public string? ClassName { get; init; }
    public string? Role { get; init; }
    public string? Label { get; init; }
    public int TabIndex { get; init; }
    public bool Accessible { get; init; }
    public bool Interactive { get; init; }
    public bool Focusable { get; init; }
    public bool Enabled { get; init; }
    public bool? Checked { get; init; }
    public float? Value { get; init; }
    public string? Accent { get; init; }
    public string? InputValue { get; init; }
    public string? Placeholder { get; init; }
    public int MaxLength { get; init; }
    public bool ReadOnly { get; init; }
    public bool Password { get; init; }
}

public sealed record UiFrame(long Version, float Width, float Height, IReadOnlyList<UiRenderNode> Nodes,
    string? FocusedId, string? ModalId);
