using System.Numerics;

namespace WolfNET.Engine.UI;

public sealed record UiShape(string Kind, IReadOnlyList<float> Points, string? Fill = null, string? Stroke = null,
    float LineWidth = 1, UiMatrix? Transform = null);

/// <summary>An immutable vector display list. Replace it to invalidate a custom widget's drawing.</summary>
public sealed class UiDrawing
{
    private static long _nextRevision;
    public long Revision { get; } = Interlocked.Increment(ref _nextRevision);
    public IReadOnlyList<UiShape> Shapes { get; }
    public UiDrawing(IEnumerable<UiShape> shapes)
    {
        var snapshot = shapes.Select(shape => shape with { Points = Array.AsReadOnly(shape.Points.ToArray()) }).ToArray();
        foreach (var shape in snapshot)
        {
            var valid = shape.Kind switch
            {
                "rect" => shape.Points.Count == 4 && shape.Points[2] >= 0 && shape.Points[3] >= 0,
                "circle" => shape.Points.Count == 3 && shape.Points[2] >= 0,
                "polyline" => shape.Points.Count >= 4 && shape.Points.Count % 2 == 0,
                "polygon" => shape.Points.Count >= 6 && shape.Points.Count % 2 == 0,
                _ => false
            };
            if (!valid || shape.Points.Any(value => !float.IsFinite(value)) || !float.IsFinite(shape.LineWidth) || shape.LineWidth < 0)
                throw new ArgumentException("UI vector geometry and stroke widths must be valid and finite.", nameof(shapes));
        }
        Shapes = Array.AsReadOnly(snapshot);
    }
}

public sealed class UiDrawingBuilder
{
    private readonly List<UiShape> _shapes = [];
    public Matrix3x2 Transform { get; set; } = Matrix3x2.Identity;
    public UiDrawingBuilder Polyline(IEnumerable<Vector2> points, string stroke, float width = 1) =>
        Shape("polyline", points.SelectMany(point => new[] { point.X, point.Y }).ToArray(), null, stroke, width);
    public UiDrawingBuilder Polygon(IEnumerable<Vector2> points, string fill, string? stroke = null, float width = 1) =>
        Shape("polygon", points.SelectMany(point => new[] { point.X, point.Y }).ToArray(), fill, stroke, width);
    public UiDrawingBuilder Rectangle(UiRect rect, string fill, string? stroke = null, float width = 1) =>
        Shape("rect", [rect.X, rect.Y, rect.Width, rect.Height], fill, stroke, width);
    public UiDrawingBuilder Circle(Vector2 center, float radius, string fill, string? stroke = null, float width = 1) =>
        Shape("circle", [center.X, center.Y, radius], fill, stroke, width);
    private UiDrawingBuilder Shape(string kind, float[] points, string? fill, string? stroke, float width)
    {
        _shapes.Add(new(kind, points, fill, stroke, width, UiMatrix.From(Transform))); return this;
    }
    public UiDrawing Build() => new(_shapes);
}
