using SkiaSharp;
using Svg.Skia;
using WolfSlop.Engine.Assets;

namespace WolfSlop.Engine.Sdl.Graphics;

internal static class AssetImage
{
    public static SKBitmap Decode(IAssetSource assets, string path)
    {
        using var stream = assets.Open(path);
        if (!path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            return SKBitmap.Decode(stream) ?? throw new InvalidDataException($"Cannot decode '{path}'.");
        using var svg = new SKSvg();
        var picture = svg.Load(stream) ?? throw new InvalidDataException($"Cannot decode '{path}'.");
        var bounds = picture.CullRect;
        var bitmap = new SKBitmap(new SKImageInfo((int)MathF.Ceiling(bounds.Width), (int)MathF.Ceiling(bounds.Height), SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Transparent); canvas.DrawPicture(picture);
        return bitmap;
    }
}
