using System.Numerics;
using System.Runtime.InteropServices;
using WolfSlop.Engine.World;

using WolfSlop.Engine.Graphics;

namespace WolfSlop.Engine.Browser.Graphics;

/// <summary>Resolves the browser's ordered sprite IDs without repeating atlas-frame selection.</summary>
internal sealed class SpriteSelection
{
    // Matches the 32-byte storage record used by both sprite shaders.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal readonly record struct VisibleSprite(Vector2 Position, uint Layer, uint OwnerCell,
        Vector2 Scale, float Elevation, uint Kind);
    private readonly List<VisibleSprite> _visible = [];
    public ReadOnlySpan<VisibleSprite> Visible => CollectionsMarshal.AsSpan(_visible);
    public int CandidatesExamined { get; private set; }

    public void Clear()
    {
        _visible.Clear();
        CandidatesExamined = 0;
    }

    public void Resolve(IReadOnlyList<Sprite> sprites, ReadOnlySpan<uint> keys, int count,
        ReadOnlySpan<int> frames, ReadOnlySpan<float> widths, IReadOnlyList<Sprite> dynamicSprites)
    {
        _visible.Clear();
        CandidatesExamined = (int)keys[1];
        for (var i = 0; i < count; i++)
        {
            var index = (int)keys[2 + i * 2];
            var sprite = index < sprites.Count ? sprites[index] : dynamicSprites[index - sprites.Count];
            var frame = frames[index];
            _visible.Add(new(sprite.Position, sprite.TextureLayer + (uint)frame, sprite.OwnerCell,
                new(widths[index], sprite.Scale.Y), sprite.Elevation, sprite.ViewKind(frame)));
        }
    }
}
