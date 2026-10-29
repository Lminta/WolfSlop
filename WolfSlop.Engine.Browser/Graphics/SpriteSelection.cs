using System.Numerics;
using System.Runtime.InteropServices;
using WolfNET.Engine.World;

using WolfNET.Engine.Graphics;

namespace WolfNET.Engine.Browser.Graphics;

/// <summary>Resolves the browser's ordered sprite IDs without repeating atlas-frame selection.</summary>
internal sealed class SpriteSelection
{
    internal sealed record AtlasView(int Index, int Frames, int Directions, float Rotation,
        float AnimationRate, double PhaseSeconds, float[] Widths, uint[] Kinds);

    public static (VisibleSprite[] Records, AtlasView[] Atlases) PrepareGpuScene(IReadOnlyList<Sprite> sprites)
    {
        var records = new VisibleSprite[sprites.Count];
        var atlases = new List<AtlasView>();
        for (var i = 0; i < sprites.Count; i++)
        {
            var sprite = sprites[i];
            records[i] = new(sprite.Position, sprite.TextureLayer, sprite.OwnerCell,
                new(sprite.ViewWidth(0), sprite.Scale.Y), sprite.Elevation, sprite.ViewKind(0));
            if (sprite.Atlas is not { } atlas) continue;
            var widths = new float[atlas.Frames.Length];
            var kinds = new uint[widths.Length];
            for (var frame = 0; frame < widths.Length; frame++)
            {
                widths[frame] = sprite.ViewWidth(frame);
                kinds[frame] = sprite.ViewKind(frame);
            }
            atlases.Add(new(i, widths.Length, atlas.Directions, sprite.RotationDegrees * MathF.PI / 180,
                sprite.Animation?.FramesPerSecond ?? 0, sprite.Animation?.PhaseSeconds ?? 0, widths, kinds));
        }
        return (records, atlases.ToArray());
    }

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
