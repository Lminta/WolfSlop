using SkiaSharp;
using WolfSlop.Engine.Sdl.Graphics;
using WolfSlop.Engine.Assets;
using WolfSlop.Engine.UI;

namespace WolfSlop.Engine.Sdl.UI;

/// <summary>Rasterizes portable UI display lists for SDL GPU composition.</summary>
public sealed class SdlUiRenderer(IAssetSource assets) : IUiRenderer, IUiTextMeasurer
{
    private readonly Dictionary<string, SKBitmap> _images = [];
    private readonly Dictionary<UiFont, SKFont> _fonts = [];
    private readonly Dictionary<string, SKTypeface> _typefaces = [];
    private SKBitmap? _bitmap;
    public SKBitmap Bitmap => _bitmap ?? throw new InvalidOperationException("Render UI first.");
    private SKFont Font(UiFont font)
    {
        if (_fonts.TryGetValue(font, out var value)) return value;
        var family = font.Family.Contains("mono") ? "RobotoMono" : "NotoSans";
        if (!_typefaces.TryGetValue(family, out var typeface))
        {
            using var stream = typeof(SdlUiRenderer).Assembly.GetManifestResourceStream($"WolfSlop.Engine.Sdl.Fonts.{family}.ttf")
                ?? throw new FileNotFoundException($"Bundled font {family} is missing.");
            typeface = SKTypeface.FromStream(stream); _typefaces[family] = typeface;
        }
        return _fonts[font] = new SKFont(typeface, font.Size) { Embolden = font.Weight >= 600, SkewX = font.Italic ? -.2f : 0 };
    }
    public float MeasureWidth(string text, UiFont font) => Font(font).MeasureText(text);
    private static SKMatrix Matrix(UiMatrix matrix) => new(matrix.A, matrix.C, matrix.E, matrix.B, matrix.D, matrix.F, 0, 0, 1);
    private static SKRect Rect(UiRect rect) => new(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height);
    private static SKColor Color(string? color, float opacity)
    {
        if (color == null) return SKColors.Transparent;
        if (color.Length == 9 && color[0] == '#')
            return new SKColor(Convert.ToByte(color[1..3], 16), Convert.ToByte(color[3..5], 16), Convert.ToByte(color[5..7], 16),
                (byte)(Convert.ToByte(color[7..9], 16) * opacity));
        var parsed = SKColor.Parse(color);
        return parsed.WithAlpha((byte)(parsed.Alpha * opacity));
    }
    public void Render(UiFrame frame)
    {
        var width = Math.Max(1, (int)MathF.Ceiling(frame.Width)); var height = Math.Max(1, (int)MathF.Ceiling(frame.Height));
        if (_bitmap == null || _bitmap.Width != width || _bitmap.Height != height)
        { _bitmap?.Dispose(); _bitmap = new(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul)); }
        using var canvas = new SKCanvas(_bitmap); canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { IsAntialias = true };
        foreach (var node in frame.Nodes)
        {
            if (node.Opacity <= 0) continue;
            canvas.Save();
            foreach (var clip in node.Clips)
            {
                canvas.SetMatrix(Matrix(clip.Transform));
                canvas.ClipRoundRect(new SKRoundRect(Rect(clip.Rect), clip.Radius), SKClipOperation.Intersect, true);
            }
            canvas.SetMatrix(Matrix(node.Transform));
            paint.Style = SKPaintStyle.Fill; paint.Color = Color(node.Background, node.Opacity);
            canvas.DrawRoundRect(new SKRect(0, 0, node.Width, node.Height), node.CornerRadius, node.CornerRadius, paint);
            if (node.BorderWidth > 0)
            {
                paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = node.BorderWidth; paint.Color = Color(node.BorderColor, node.Opacity);
                canvas.DrawRoundRect(new SKRect(node.BorderWidth / 2, node.BorderWidth / 2, node.Width - node.BorderWidth / 2, node.Height - node.BorderWidth / 2), node.CornerRadius, node.CornerRadius, paint);
            }
            if (node.Value is { } progress)
            {
                paint.Style = SKPaintStyle.Fill; paint.Color = Color(node.Accent, node.Opacity);
                canvas.DrawRect(0, 0, node.Width * progress, node.Height, paint);
            }
            if (node.Source is { } source)
            {
                if (!_images.TryGetValue(source, out var image))
                { image = AssetImage.Decode(assets, source); _images[source] = image; }
                var crop = node.SourceRect;
                var src = new SKRect(crop.X * image.Width, crop.Y * image.Height, (crop.X + crop.Width) * image.Width, (crop.Y + crop.Height) * image.Height);
                var dst = new SKRect(0, 0, node.Width, node.Height);
                if (node.ImageFit != UiImageFit.Stretch)
                {
                    var scale = node.ImageFit == UiImageFit.Contain ? Math.Min(dst.Width / src.Width, dst.Height / src.Height) : Math.Max(dst.Width / src.Width, dst.Height / src.Height);
                    var w = src.Width * scale; var h = src.Height * scale;
                    dst = new((node.Width - w) / 2, (node.Height - h) / 2, (node.Width + w) / 2, (node.Height + h) / 2);
                    canvas.ClipRect(new SKRect(0, 0, node.Width, node.Height));
                }
                paint.Style = SKPaintStyle.Fill; paint.Color = SKColors.White.WithAlpha((byte)(255 * node.Opacity));
                canvas.DrawBitmap(image, src, dst, paint);
            }
            if (node.TextLayout is { } layout && node.Font is { } font)
            {
                var nativeFont = Font(font); var y = node.Padding.Top + font.Size;
                foreach (var line in layout.Lines)
                {
                    var available = node.Width - node.Padding.Left - node.Padding.Right;
                    var x = node.Padding.Left + (node.TextAlignment == UiAlignment.Center ? (available - line.Width) / 2 : node.TextAlignment == UiAlignment.End ? available - line.Width : 0);
                    if (node.TextOutlineWidth > 0)
                    {
                        paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = node.TextOutlineWidth * 2; paint.Color = Color(node.TextOutlineColor, node.Opacity);
                        canvas.DrawText(line.Text, x, y, nativeFont, paint);
                    }
                    paint.Style = SKPaintStyle.Fill; paint.Color = Color(node.Color, node.Opacity);
                    canvas.DrawText(line.Text, x, y, nativeFont, paint); y += layout.LineHeight;
                }
            }
            if (node.Drawing is { } drawing)
            {
                if (node.ViewBox.Width > 0 && node.ViewBox.Height > 0)
                { canvas.Scale(node.Width / node.ViewBox.Width, node.Height / node.ViewBox.Height); canvas.Translate(-node.ViewBox.X, -node.ViewBox.Y); }
                var transform = Matrix(node.DrawingTransform); canvas.Concat(in transform);
                foreach (var shape in drawing.Shapes)
                {
                    canvas.Save(); if (shape.Transform is { } matrix) { var native = Matrix(matrix); canvas.Concat(in native); }
                    using var path = new SKPath(); var p = shape.Points;
                    switch (shape.Kind)
                    {
                        case "rect": path.AddRect(new(p[0], p[1], p[0] + p[2], p[1] + p[3])); break;
                        case "circle": path.AddCircle(p[0], p[1], p[2]); break;
                        default: path.MoveTo(p[0], p[1]); for (var i = 2; i < p.Count; i += 2) path.LineTo(p[i], p[i + 1]); if (shape.Kind == "polygon") path.Close(); break;
                    }
                    if (shape.Fill != null) { paint.Style = SKPaintStyle.Fill; paint.Color = Color(shape.Fill, node.Opacity); canvas.DrawPath(path, paint); }
                    if (shape.Stroke != null) { paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = shape.LineWidth; paint.Color = Color(shape.Stroke, node.Opacity); canvas.DrawPath(path, paint); }
                    canvas.Restore();
                }
            }
            canvas.Restore();
        }
    }
    public void Dispose()
    {
        _bitmap?.Dispose(); foreach (var image in _images.Values) image.Dispose();
        foreach (var font in _fonts.Values) font.Dispose();
        foreach (var typeface in _typefaces.Values) typeface.Dispose();
    }
}
