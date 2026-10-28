using System.Globalization;
using System.Numerics;

namespace WolfNET.Engine.UI;

public interface IUiTextMeasurer
{
    long Version => 0;
    float MeasureWidth(string text, UiFont font);
}

/// <summary>Deterministic fallback for headless layout. Browser hosts use actual font metrics.</summary>
public sealed class UiApproximateTextMeasurer : IUiTextMeasurer
{
    public static UiApproximateTextMeasurer Instance { get; } = new();
    public float MeasureWidth(string text, UiFont font) => StringInfo.ParseCombiningCharacters(text).Length * font.Size * .6f;
}

public sealed record UiTextLine(string Text, float Width);
public sealed record UiTextLayout(IReadOnlyList<UiTextLine> Lines, float Width, float Height, float LineHeight)
{
    public Vector2 Size => new(Width, Height);
    public static UiTextLayout Measure(string text, UiFont font, float width, int maxLines, IUiTextMeasurer metrics)
    {
        if (!float.IsFinite(font.Size) || font.Size <= 0 || string.IsNullOrWhiteSpace(font.Family) || font.Weight is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(font));
        var lines = new List<string>();
        width = MathF.Max(0, width);
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            if (paragraph.Length == 0) { lines.Add(""); continue; }
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (metrics.MeasureWidth(candidate, font) <= width) { line = candidate; continue; }
                if (line.Length > 0) { lines.Add(line); line = ""; }
                var graphemes = StringInfo.GetTextElementEnumerator(word);
                while (graphemes.MoveNext())
                {
                    var glyph = graphemes.GetTextElement();
                    if (line.Length > 0 && metrics.MeasureWidth(line + glyph, font) > width)
                    { lines.Add(line); line = ""; }
                    line += glyph;
                }
            }
            lines.Add(line);
        }
        maxLines = Math.Max(1, maxLines);
        if (lines.Count > maxLines)
        {
            lines.RemoveRange(maxLines, lines.Count - maxLines);
            var last = lines[^1];
            while (last.Length > 0 && metrics.MeasureWidth(last + "…", font) > width)
            {
                var indices = StringInfo.ParseCombiningCharacters(last);
                last = last[..indices[^1]];
            }
            lines[^1] = last + "…";
        }
        var result = lines.Select(line => new UiTextLine(line, metrics.MeasureWidth(line, font))).ToArray();
        var lineHeight = font.Size * 1.25f;
        return new(Array.AsReadOnly(result), result.Length == 0 ? 0 : result.Max(line => line.Width), result.Length * lineHeight, lineHeight);
    }
}
