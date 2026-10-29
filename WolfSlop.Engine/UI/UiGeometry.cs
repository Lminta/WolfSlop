using System.Numerics;

namespace WolfSlop.Engine.UI;

public readonly record struct UiRect(float X, float Y, float Width, float Height)
{
    public bool Contains(Vector2 point) => Width > 0 && Height > 0 &&
        point.X >= X && point.Y >= Y && point.X < X + Width && point.Y < Y + Height;
    [System.Text.Json.Serialization.JsonIgnore]
    public Vector2 Size => new(Width, Height);
    public bool Contains(Vector2 point, float radius)
    {
        if (!Contains(point)) return false;
        radius = Math.Clamp(radius, 0, MathF.Min(Width, Height) / 2);
        var center = Vector2.Clamp(point, new(X + radius, Y + radius), new(X + Width - radius, Y + Height - radius));
        return Vector2.DistanceSquared(point, center) <= radius * radius;
    }
}

public readonly record struct UiThickness(float Left, float Top, float Right, float Bottom)
{
    public UiThickness(float all) : this(all, all, all, all) { }
    public UiThickness(float horizontal, float vertical) : this(horizontal, vertical, horizontal, vertical) { }
    [System.Text.Json.Serialization.JsonIgnore]
    public Vector2 Size => new(Left + Right, Top + Bottom);
}

public enum UiUnit { Auto, Pixels, Percent }
public readonly record struct UiLength(float Value, UiUnit Unit = UiUnit.Pixels)
{
    public static UiLength Auto => new(0, UiUnit.Auto);
    public static UiLength Percent(float fraction) => new(fraction, UiUnit.Percent);
    public static implicit operator UiLength(float pixels) => new(pixels);
    internal float Resolve(float available, float automatic) => Unit switch
    {
        UiUnit.Auto => automatic,
        UiUnit.Percent => available * Value,
        _ => Value
    };
}

public enum UiLayoutMode { Overlay, Row, Column }
public enum UiAlignment { Start, Center, End, Stretch }
public enum UiJustify { Start, Center, End, SpaceBetween }
public enum UiScaleMode { Reference, Pixels }
public enum UiImageFit { Stretch, Contain, Cover }

/// <summary>Sizes and offsets use logical pixels; percent lengths are fractions of the parent.</summary>
public sealed record UiStyle
{
    public UiLength Width { get; init; } = UiLength.Auto;
    public UiLength Height { get; init; } = UiLength.Auto;
    public float? AspectRatio { get; init; }
    public Vector2 MinSize { get; init; }
    public Vector2 MaxSize { get; init; } = new(float.MaxValue);
    public Vector2 Anchor { get; init; }
    public Vector2 Pivot { get; init; }
    public Vector2 Position { get; init; }
    public UiThickness Margin { get; init; }
    public UiThickness Padding { get; init; }
    public UiLayoutMode Layout { get; init; }
    public UiAlignment Alignment { get; init; } = UiAlignment.Start;
    public UiJustify Justify { get; init; }
    public float Gap { get; init; }
    public float Grow { get; init; }
    public bool Absolute { get; init; }
    public bool Clip { get; init; }
    public float Opacity { get; init; } = 1;
    public float Rotation { get; init; }
    public Vector2 Scale { get; init; } = Vector2.One;
    public Vector2 TransformOrigin { get; init; } = new(.5f);
    public int ZIndex { get; init; }
    public string? Color { get; init; }
    public string? Background { get; init; }
    public string? BorderColor { get; init; }
    public float BorderWidth { get; init; }
    public float CornerRadius { get; init; }
    public string? TextOutlineColor { get; init; }
    public float TextOutlineWidth { get; init; }
    public static UiStyle Fill { get; } = new() { Width = UiLength.Percent(1), Height = UiLength.Percent(1) };
}

public sealed record UiFont(string Family = "system-ui, sans-serif", float Size = 22, int Weight = 400, bool Italic = false);
public sealed record UiTheme
{
    public UiFont Font { get; init; } = new();
    public string Foreground { get; init; } = "#e9f0f5";
    public string Button { get; init; } = "#23313d";
    public string ButtonHover { get; init; } = "#36566c";
    public string ButtonPressed { get; init; } = "#194a64";
    public string Accent { get; init; } = "#45bfff";
    public string Border { get; init; } = "#82919e";
    public string Disabled { get; init; } = "#66717a";
}

/// <summary>Serialization-friendly affine transform; compatible with Canvas 2D.</summary>
public readonly record struct UiMatrix(float A, float B, float C, float D, float E, float F)
{
    public static UiMatrix Identity => From(Matrix3x2.Identity);
    public static UiMatrix From(Matrix3x2 matrix) => new(matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.M31, matrix.M32);
    [System.Text.Json.Serialization.JsonIgnore]
    public Matrix3x2 Matrix => new(A, B, C, D, E, F);
}
