using System.Numerics;

namespace WolfSlop.Engine.UI;

public class UiText(string? id = null) : UiElement(id)
{
    private string _text = "";
    private UiFont? _font;
    private bool _wrap;
    private int _maxLines = int.MaxValue;
    private UiAlignment _textAlignment;
    private sealed record LayoutCache(string Text, UiFont Font, float Width, int Lines,
        IUiTextMeasurer Metrics, long Version, UiTextLayout Layout);
    private LayoutCache? _layout, _alternateLayout;
    public string Text
    {
        get => _text;
        set { value ??= ""; if (_text == value) return; Set(ref _text, value); TextChanged?.Invoke(value); }
    }
    public event Action<string>? TextChanged;
    public UiFont? Font { get => _font; set => Set(ref _font, value); }
    public bool Wrap { get => _wrap; set => Set(ref _wrap, value); }
    public int MaxLines { get => _maxLines; set => Set(ref _maxLines, Math.Max(1, value)); }
    public UiAlignment TextAlignment { get => _textAlignment; set => Set(ref _textAlignment, value); }
    internal override string Kind => "text";
    internal override Vector2 Measure(Vector2 available, IUiTextMeasurer metrics, UiTheme theme) =>
        Layout(Text, Font ?? theme.Font, Wrap ? available.X : float.MaxValue, metrics).Size;

    internal UiTextLayout Layout(string text, UiFont font, float width, IUiTextMeasurer metrics)
    {
        bool Matches(LayoutCache? entry) => entry != null && entry.Text == text && entry.Font == font &&
            entry.Width == width && entry.Lines == MaxLines && ReferenceEquals(entry.Metrics, metrics) && entry.Version == metrics.Version;
        if (Matches(_layout)) return _layout!.Layout;
        if (Matches(_alternateLayout))
        {
            (_layout, _alternateLayout) = (_alternateLayout, _layout);
            return _layout!.Layout;
        }
        var result = UiTextLayout.Measure(text, font, width, MaxLines, metrics);
        _alternateLayout = _layout;
        _layout = new(text, font, width, MaxLines, metrics, metrics.Version, result);
        return result;
    }
}

/// <summary>A single-line editor. The browser backend uses native editing, selection and IME.</summary>
public sealed class UiTextInput : UiText
{
    private string _placeholder = "";
    private bool _readOnly, _password;
    private int _maxLength = 4096;
    public string Placeholder { get => _placeholder; set => Set(ref _placeholder, value ?? ""); }
    public bool ReadOnly { get => _readOnly; set => Set(ref _readOnly, value); }
    public bool Password { get => _password; set => Set(ref _password, value); }
    public int MaxLength
    {
        get => _maxLength;
        set { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value); Set(ref _maxLength, value); }
    }
    public event Action<string>? Submitted;
    internal override string Kind => "input";
    internal void Submit() => Submitted?.Invoke(Text);
    public UiTextInput(string? id = null) : base(id)
    {
        Role = "textbox"; Focusable = true; BlocksInput = true;
        Style = new() { Width = 240, Height = 42, Padding = new(10, 6), BorderWidth = 1, CornerRadius = 6, Clip = true };
    }
}

public class UiButton : UiText
{
    internal override string Kind => "button";
    public UiButton(string? id = null) : base(id)
    {
        Focusable = true; BlocksInput = true; Role = "button";
        TextAlignment = UiAlignment.Center;
        Style = new() { Padding = new(18, 10), CornerRadius = 6, BorderWidth = 1 };
    }
}

public sealed class UiToggle(string? id = null) : UiButton(id)
{
    private bool _checked;
    public bool Checked { get => _checked; set { if (_checked == value) return; Set(ref _checked, value); Changed?.Invoke(value); } }
    public event Action<bool>? Changed;
    internal override string Kind => "toggle";
    internal override void Activate(UiPointerEvent e) { Checked = !Checked; base.Activate(e); }
}

public sealed class UiImage(string? id = null) : UiElement(id)
{
    private string _source = "";
    private UiRect _sourceRect = new(0, 0, 1, 1);
    private Vector2 _intrinsicSize = new(64);
    private UiImageFit _fit;
    public string Source { get => _source; set => Set(ref _source, value ?? ""); }
    public UiRect SourceRect { get => _sourceRect; set => Set(ref _sourceRect, value); }
    public Vector2 IntrinsicSize { get => _intrinsicSize; set => Set(ref _intrinsicSize, value); }
    public UiImageFit Fit { get => _fit; set => Set(ref _fit, value); }
    internal override string Kind => "image";
    internal override Vector2 Measure(Vector2 available, IUiTextMeasurer metrics, UiTheme theme) => IntrinsicSize * new Vector2(SourceRect.Width, SourceRect.Height);
}

public class UiProgressBar : UiElement
{
    public UiProgressBar(string? id = null) : base(id) => Role = "progressbar";
    private float _value;
    public float Value
    {
        get => _value;
        set
        {
            if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            var next = Math.Clamp(value, 0, 1);
            if (_value == next) return;
            Set(ref _value, next); ValueChanged?.Invoke(next);
        }
    }
    public event Action<float>? ValueChanged;
    internal override string Kind => "progress";
}

public sealed class UiSlider : UiProgressBar
{
    public float Step { get; init; } = .05f;
    internal override string Kind => "slider";
    public UiSlider(string? id = null) : base(id)
    {
        Focusable = true; BlocksInput = true; Role = "slider";
        Style = new() { Width = 200, Height = 28, CornerRadius = 4 };
    }
    internal void SetFromPointer(Vector2 point)
    {
        if (!Matrix3x2.Invert(WorldTransform, out var inverse) || Bounds.Width <= 0) return;
        Value = Vector2.Transform(point, inverse).X / Bounds.Width;
    }
}

public sealed class UiCanvas(string? id = null) : UiElement(id)
{
    private UiDrawing _drawing = new([]);
    private UiRect _viewBox = new(0, 0, 100, 100);
    private Matrix3x2 _drawingTransform = Matrix3x2.Identity;
    public UiDrawing Drawing { get => _drawing; set => Set(ref _drawing, value ?? throw new ArgumentNullException(nameof(value))); }
    public UiRect ViewBox { get => _viewBox; set => Set(ref _viewBox, value); }
    // View changes reuse the immutable drawing and its browser-side geometry.
    public Matrix3x2 DrawingTransform { get => _drawingTransform; set => Set(ref _drawingTransform, value); }
    internal override string Kind => "canvas";
}

public sealed class UiScrollView : UiElement
{
    private float _offset;
    public float Extent { get; internal set; }
    public float Offset
    {
        get => _offset;
        set
        {
            if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            Set(ref _offset, Math.Clamp(value, 0, Extent));
        }
    }
    internal override string Kind => "scroll";
    public UiScrollView(string? id = null) : base(id)
    {
        BlocksInput = true;
        Style = new() { Layout = UiLayoutMode.Column, Clip = true };
    }
}
