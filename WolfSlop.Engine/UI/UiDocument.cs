using System.Numerics;

namespace WolfNET.Engine.UI;

/// <summary>Owns layout, draw order, focus and input for a retained UI tree. Contains no browser APIs.</summary>
public sealed class UiDocument : IDisposable
{
    private readonly Dictionary<string, UiElement> _elements = new(StringComparer.Ordinal);
    private readonly Dictionary<UiElement, Vector2> _desired = [];
    private UiTheme _theme = new();
    private Vector2 _referenceSize = new(1920, 1080), _lastViewport;
    private UiScaleMode _scaleMode;
    private UiFrame? _frame;
    private IUiTextMeasurer? _lastMetrics;
    private UiElement? _modal;
    private UiElement? _focusBeforeModal;
    private bool _disposed;
    private bool _layoutDirty = true;
    private long _lastMetricsVersion = -1;
    public bool IsDisposed => _disposed;
    public UiPanel Root { get; }
    public UiInputRouter Input { get; }
    public long Version { get; private set; }
    public UiTheme Theme { get => _theme; set { _theme = value ?? throw new ArgumentNullException(nameof(value)); Invalidate(); } }
    public Vector2 ReferenceSize
    {
        get => _referenceSize;
        set { if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || value.X <= 0 || value.Y <= 0) throw new ArgumentOutOfRangeException(nameof(value)); _referenceSize = value; Invalidate(); }
    }
    public UiScaleMode ScaleMode { get => _scaleMode; set { _scaleMode = value; Invalidate(); } }
    public UiElement? ModalRoot
    {
        get => _modal;
        set
        {
            if (value != null && !ReferenceEquals(value.Document, this)) throw new ArgumentException("Modal root must belong to this document.");
            if (ReferenceEquals(_modal, value)) return;
            if (_modal == null && value != null) _focusBeforeModal = Input.Focused;
            _modal = value; Input.Cancel(); Input.Focus(null); Invalidate();
            if (value != null) Input.MoveFocus();
            else { Input.Focus(_focusBeforeModal); _focusBeforeModal = null; }
        }
    }
    public event Action? Changed;
    public UiDocument()
    {
        Root = new("ui-root") { Style = UiStyle.Fill };
        Input = new(this);
        Attach(Root);
    }
    public UiElement? Find(string id) => _elements.GetValueOrDefault(id);
    public IEnumerable<string> ImageSources => Root.DescendantsAndSelf().OfType<UiImage>()
        .Select(image => image.Source).Where(source => !string.IsNullOrWhiteSpace(source)).Distinct(StringComparer.Ordinal);
    public void Invalidate()
    {
        if (_disposed) return;
        _layoutDirty = true;
        InvalidateVisual();
    }
    internal void InvalidateVisual()
    {
        if (_disposed) return;
        Version++; Changed?.Invoke();
    }
    internal void Attach(UiElement subtree)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var nodes = subtree.DescendantsAndSelf().ToArray();
        if (nodes.Select(node => node.Id).Distinct(StringComparer.Ordinal).Count() != nodes.Length || nodes.Any(node => _elements.ContainsKey(node.Id)))
            throw new InvalidOperationException("UI IDs must be unique within a document.");
        foreach (var node in nodes) { _elements.Add(node.Id, node); node.Document = this; }
    }
    internal void Detach(UiElement subtree)
    {
        var nodes = subtree.DescendantsAndSelf().ToArray();
        if (nodes.Contains(ModalRoot)) ModalRoot = null;
        if (nodes.Contains(Input.Focused)) Input.Focus(null);
        Input.Detach(nodes);
        foreach (var node in nodes) { _elements.Remove(node.Id); node.Document = null; }
    }
    internal bool Available(UiElement node)
    {
        if (!ReferenceEquals(node.Document, this)) return false;
        for (var current = node; current != null; current = current.Parent)
            if (!current.Visible || !current.Enabled || current.Style.Opacity <= 0 || current.Style.Scale.X == 0 || current.Style.Scale.Y == 0) return false;
        return ModalRoot == null || node.DescendantsAndSelf().Contains(ModalRoot) || IsWithin(node, ModalRoot);
    }
    internal static bool IsWithin(UiElement node, UiElement ancestor)
    {
        for (var current = node; current != null; current = current.Parent)
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    public UiFrame BuildFrame(Vector2 viewport, IUiTextMeasurer? metrics = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!float.IsFinite(viewport.X) || !float.IsFinite(viewport.Y) || viewport.X <= 0 || viewport.Y <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewport));
        metrics ??= UiApproximateTextMeasurer.Instance;
        if (_modal != null)
            for (var current = _modal; current != null; current = current.Parent)
                if (!current.Visible || current.Style.Opacity <= 0) { ModalRoot = null; break; }
        if (Input.Focused is { } focus && !Available(focus)) Input.Focus(null);
        var viewportOrMetricsChanged = viewport != _lastViewport || !ReferenceEquals(metrics, _lastMetrics) || metrics.Version != _lastMetricsVersion;
        if (_frame?.Version == Version && !viewportOrMetricsChanged) return _frame;
        _lastViewport = viewport; _lastMetrics = metrics;
        _lastMetricsVersion = metrics.Version;
        var scale = ScaleMode == UiScaleMode.Pixels ? 1 : MathF.Min(viewport.X / ReferenceSize.X, viewport.Y / ReferenceSize.Y);
        var logical = viewport / scale;
        if (_layoutDirty || viewportOrMetricsChanged)
        {
            _desired.Clear();
            Measure(Root, logical, metrics);
            Arrange(Root, new(0, 0, logical.X, logical.Y));
            _layoutDirty = false;
        }
        var nodes = new List<UiRenderNode>();
        Emit(Root, Matrix3x2.Identity, [new(new(0, 0, logical.X, logical.Y), UiMatrix.Identity)], 1, Theme.Foreground, true, metrics, nodes);
        return _frame = new(Version, logical.X, logical.Y, nodes.AsReadOnly(), Input.Focused?.Id, ModalRoot?.Id);
    }

    private Vector2 Measure(UiElement node, Vector2 available, IUiTextMeasurer metrics)
    {
        if (!node.Visible) return _desired[node] = Vector2.Zero;
        var style = node.Style;
        Validate(style);
        var inner = Vector2.Max(Vector2.Zero, Vector2.Clamp(new Vector2(style.Width.Resolve(available.X, available.X),
            style.Height.Resolve(available.Y, available.Y)), style.MinSize, style.MaxSize) - style.Padding.Size);
        var desired = node.Measure(inner, metrics, Theme);
        var flow = 0;
        foreach (var child in node.Children.Where(child => child.Visible))
        {
            var childSize = Measure(child, inner, metrics) + child.Style.Margin.Size;
            if (style.Layout == UiLayoutMode.Overlay || child.Style.Absolute) desired = Vector2.Max(desired, childSize);
            else
            {
                if (style.Layout == UiLayoutMode.Row) desired = new(desired.X + childSize.X, MathF.Max(desired.Y, childSize.Y));
                else desired = new(MathF.Max(desired.X, childSize.X), desired.Y + childSize.Y);
                flow++;
            }
        }
        if (flow > 1) desired += style.Layout == UiLayoutMode.Row ? new Vector2(style.Gap * (flow - 1), 0) : new Vector2(0, style.Gap * (flow - 1));
        desired += style.Padding.Size;
        var size = new Vector2(style.Width.Resolve(available.X, desired.X), style.Height.Resolve(available.Y, desired.Y));
        if (style.AspectRatio is { } ratio)
        {
            if (style.Height.Unit == UiUnit.Auto) size.Y = size.X / ratio;
            else if (style.Width.Unit == UiUnit.Auto) size.X = size.Y * ratio;
        }
        return _desired[node] = Vector2.Clamp(Vector2.Max(Vector2.Zero, size), style.MinSize, style.MaxSize);
    }

    private void Arrange(UiElement node, UiRect rect)
    {
        node.Bounds = rect;
        var style = node.Style;
        var origin = new Vector2(style.Padding.Left, style.Padding.Top);
        var available = Vector2.Max(Vector2.Zero, rect.Size - style.Padding.Size);
        var flow = node.Children.Where(child => child.Visible && !child.Style.Absolute && style.Layout != UiLayoutMode.Overlay).ToArray();
        var row = style.Layout == UiLayoutMode.Row;
        float Main(Vector2 value) => row ? value.X : value.Y;
        float Cross(Vector2 value) => row ? value.Y : value.X;
        var occupied = flow.Sum(child => Main(_desired[child] + child.Style.Margin.Size)) + MathF.Max(0, flow.Length - 1) * style.Gap;
        if (node is UiScrollView scroll)
        {
            scroll.Extent = MathF.Max(0, occupied - available.Y);
            scroll.Offset = scroll.Offset;
            origin.Y -= scroll.Offset;
        }
        var remainder = MathF.Max(0, Main(available) - occupied);
        var grow = flow.Sum(child => MathF.Max(0, child.Style.Grow));
        var cursor = grow > 0 ? 0 : style.Justify switch { UiJustify.Center => remainder / 2, UiJustify.End => remainder, _ => 0 };
        var gap = style.Gap + (grow == 0 && style.Justify == UiJustify.SpaceBetween && flow.Length > 1 ? remainder / (flow.Length - 1) : 0);
        foreach (var child in node.Children.Where(child => child.Visible))
        {
            var childStyle = child.Style;
            var size = _desired[child];
            var margin = childStyle.Margin;
            Vector2 position;
            if (style.Layout == UiLayoutMode.Overlay || childStyle.Absolute)
                position = origin + (available - margin.Size) * childStyle.Anchor + childStyle.Position - size * childStyle.Pivot + new Vector2(margin.Left, margin.Top);
            else
            {
                if (grow > 0) size += row ? new Vector2(remainder * childStyle.Grow / grow, 0) : new Vector2(0, remainder * childStyle.Grow / grow);
                if (style.Alignment == UiAlignment.Stretch && (row ? childStyle.Height : childStyle.Width).Unit == UiUnit.Auto)
                    size = row ? new(size.X, MathF.Max(0, available.Y - margin.Size.Y)) : new(MathF.Max(0, available.X - margin.Size.X), size.Y);
                var spare = MathF.Max(0, Cross(available - size - margin.Size));
                var across = style.Alignment switch { UiAlignment.Center => spare / 2, UiAlignment.End => spare, _ => 0 };
                position = origin + (row ? new Vector2(cursor + margin.Left, across + margin.Top) : new Vector2(across + margin.Left, cursor + margin.Top)) + childStyle.Position;
                cursor += Main(size + margin.Size) + gap;
            }
            size = Vector2.Clamp(size, childStyle.MinSize, childStyle.MaxSize);
            Arrange(child, new(position.X, position.Y, size.X, size.Y));
        }
    }

    private void Emit(UiElement node, Matrix3x2 parent, IReadOnlyList<UiClip> inheritedClips, float opacity,
        string color, bool enabled, IUiTextMeasurer metrics, List<UiRenderNode> output)
    {
        if (!node.Visible) return;
        var style = node.Style;
        var origin = style.TransformOrigin * node.Bounds.Size;
        var transform = Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale(style.Scale) *
            Matrix3x2.CreateRotation(style.Rotation) * Matrix3x2.CreateTranslation(origin + new Vector2(node.Bounds.X, node.Bounds.Y)) * parent;
        node.WorldTransform = transform;
        opacity *= style.Opacity; enabled &= node.Enabled;
        color = style.Color ?? color;
        var clips = style.Clip ? inheritedClips.Append(new UiClip(new(0, 0, node.Bounds.Width, node.Bounds.Height), UiMatrix.From(transform), style.CornerRadius)).ToArray() : inheritedClips;
        var button = node as UiButton;
        var background = style.Background ?? (button == null ? node is UiTextInput ? Theme.Button : null : node.IsPressed ? Theme.ButtonPressed : node.IsHovered ? Theme.ButtonHover : Theme.Button);
        var font = (node as UiText)?.Font ?? Theme.Font;
        var text = node is UiToggle toggle ? (toggle.Checked ? "✓ " : "□ ") + toggle.Text : (node as UiText)?.Text;
        if (node is UiTextInput input) text = input.Text.Length == 0 ? input.Placeholder : input.Password ? new string('•', input.Text.Length) : input.Text;
        var layout = text == null ? null : ((UiText)node).Layout(text, font, node is UiText { Wrap: true }
            ? MathF.Max(0, node.Bounds.Width - style.Padding.Size.X) : float.MaxValue, metrics);
        output.Add(new()
        {
            Id = node.Id, ParentId = node.Parent?.Id, Kind = node.Kind,
            Width = node.Bounds.Width, Height = node.Bounds.Height, Transform = UiMatrix.From(transform), Clips = clips,
            Opacity = opacity, Enabled = enabled, Color = enabled ? color : Theme.Disabled, Background = background,
            BorderColor = node.IsFocused ? Theme.Accent : style.BorderColor ?? (button == null && node is not UiTextInput ? null : Theme.Border),
            BorderWidth = style.BorderWidth, CornerRadius = style.CornerRadius, Padding = style.Padding,
            TextOutlineColor = style.TextOutlineColor, TextOutlineWidth = style.TextOutlineWidth,
            Content = text, Font = text == null ? null : font, TextLayout = layout, TextAlignment = (node as UiText)?.TextAlignment ?? UiAlignment.Start,
            Source = (node as UiImage)?.Source, SourceRect = (node as UiImage)?.SourceRect ?? new(0, 0, 1, 1), ImageFit = (node as UiImage)?.Fit ?? UiImageFit.Stretch,
            Drawing = (node as UiCanvas)?.Drawing, ViewBox = (node as UiCanvas)?.ViewBox ?? default,
            DrawingTransform = node is UiCanvas canvas ? UiMatrix.From(canvas.DrawingTransform) : UiMatrix.Identity,
            ClassName = node.ClassName, Role = node.Role ?? (node.Label != null && node is UiImage or UiCanvas ? "img" : null),
            Label = node.Label, Focusable = node.Focusable, TabIndex = node.TabIndex,
            Interactive = node.Interactive && enabled && opacity > 0,
            Accessible = node.ClassName != null || node.Role != null || node.Label != null || node is UiText,
            Checked = (node as UiToggle)?.Checked, Value = (node as UiProgressBar)?.Value, Accent = Theme.Accent,
            InputValue = (node as UiTextInput)?.Text, Placeholder = (node as UiTextInput)?.Placeholder,
            MaxLength = (node as UiTextInput)?.MaxLength ?? 0, ReadOnly = (node as UiTextInput)?.ReadOnly ?? false,
            Password = (node as UiTextInput)?.Password ?? false
        });
        foreach (var child in node.Children.OrderBy(child => child.Style.ZIndex)) Emit(child, transform, clips, opacity, color, enabled, metrics, output);
    }

    internal UiElement? HitTest(Vector2 point)
    {
        if (_frame == null) return null;
        foreach (var node in _frame.Nodes.Reverse())
        {
            if (!node.Interactive || node.Opacity <= 0 || !Matrix3x2.Invert(node.Transform.Matrix, out var inverse) ||
                !new UiRect(0, 0, node.Width, node.Height).Contains(Vector2.Transform(point, inverse))) continue;
            if (node.Clips.Any(clip => !Matrix3x2.Invert(clip.Transform.Matrix, out var matrix) || !clip.Rect.Contains(Vector2.Transform(point, matrix), clip.Radius))) continue;
            var element = Find(node.Id);
            if (element == null || !Available(element) || !element.Interactive) continue;
            if (ModalRoot == null || IsWithin(element, ModalRoot)) return element;
        }
        return ModalRoot;
    }

    private static void Validate(UiStyle style)
    {
        if (style.AspectRatio is { } ratio && (!float.IsFinite(ratio) || ratio <= 0) ||
            !float.IsFinite(style.Opacity) || style.Opacity < 0 || style.Opacity > 1 || !float.IsFinite(style.Rotation) ||
            !float.IsFinite(style.Gap) || style.Gap < 0 || !float.IsFinite(style.Grow) || style.Grow < 0 ||
            !float.IsFinite(style.Width.Value) || !float.IsFinite(style.Height.Value) || style.Width.Value < 0 || style.Height.Value < 0 ||
            !float.IsFinite(style.Position.X) || !float.IsFinite(style.Position.Y) || !float.IsFinite(style.Scale.X) || !float.IsFinite(style.Scale.Y) ||
            !float.IsFinite(style.Anchor.X) || !float.IsFinite(style.Anchor.Y) || !float.IsFinite(style.Pivot.X) || !float.IsFinite(style.Pivot.Y) ||
            !float.IsFinite(style.TransformOrigin.X) || !float.IsFinite(style.TransformOrigin.Y) ||
            !float.IsFinite(style.MinSize.X) || !float.IsFinite(style.MinSize.Y) || !float.IsFinite(style.MaxSize.X) || !float.IsFinite(style.MaxSize.Y) ||
            !float.IsFinite(style.CornerRadius) || style.CornerRadius < 0 || !float.IsFinite(style.BorderWidth) || style.BorderWidth < 0 ||
            !float.IsFinite(style.TextOutlineWidth) || style.TextOutlineWidth < 0 ||
            !float.IsFinite(style.Padding.Left) || !float.IsFinite(style.Padding.Top) || !float.IsFinite(style.Padding.Right) || !float.IsFinite(style.Padding.Bottom) ||
            style.Padding.Left < 0 || style.Padding.Top < 0 || style.Padding.Right < 0 || style.Padding.Bottom < 0 ||
            style.MinSize.X < 0 || style.MinSize.Y < 0 || style.MaxSize.X < style.MinSize.X || style.MaxSize.Y < style.MinSize.Y)
            throw new ArgumentException("UI layout sizes, transforms and opacity must be finite and valid.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Input.Cancel(); Input.Focus(null);
        foreach (var node in _elements.Values) node.Document = null;
        _elements.Clear(); _frame = null; Changed = null;
    }
}
