using System.Numerics;
using System.Runtime.InteropServices;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Engine.World;

using WolfNET.Engine.Graphics;

namespace WolfNET.Engine.Browser.Graphics;

/// <summary>Keeps the numeric scenery query in the browser; C# resolves the selected artwork.</summary>
internal sealed class BrowserSpriteSelection(JSObject module) : IDisposable
{
    private IReadOnlyList<Sprite>? _source;
    private Float32Array? _positions;
    private Uint32Array? _widthOverrides;
    private Uint32Array? _output;
    private Float32Array? _dynamicUpload;
    private byte[] _dynamicBytes = [];
    private byte[] _outputBytes = [], _widthBytes = [];
    private readonly List<int> _atlasIndices = [];
    private bool _widthsUploaded;
    private int[] _frames = [];
    private float[] _widths = [];

    private void Synchronize(IReadOnlyList<Sprite> sprites)
    {
        if (ReferenceEquals(_source, sprites)) return;
        ReleaseScene();
        _source = sprites;
        var bytes = new byte[Math.Max(1, sprites.Count) * 16];
        var positions = MemoryMarshal.Cast<byte, float>(bytes.AsSpan());
        _frames = new int[sprites.Count + 3];
        _widths = new float[sprites.Count + 3];
        for (var i = 0; i < sprites.Count; i++)
        {
            var sprite = sprites[i];
            positions[i * 4] = sprite.Position.X; positions[i * 4 + 1] = sprite.Position.Y;
            positions[i * 4 + 2] = _widths[i] = sprite.Scale.X;
            if (sprite.Atlas is { } atlas)
            {
                _atlasIndices.Add(i);
            }
            positions[i * 4 + 3] = sprite.MaximumViewWidth;
        }
        _positions = new Float32Array(positions.Length);
        _positions.WriteBytes(bytes);
        module.JSRef!.CallVoid("prepare", _positions, sprites.Count);
        _widthBytes = new byte[Math.Max(1, _atlasIndices.Count) * 8];
        _widthOverrides = new Uint32Array(_widthBytes.Length / 4);
        _outputBytes = new byte[(sprites.Count + 4) * 8];
        _output = new Uint32Array(_outputBytes.Length / 4);
    }

    public void Select(SpriteSelection selection, IReadOnlyList<Sprite> sprites, Vector2 position, Vector2 direction,
        Vector2 plane, float range, bool cullView, double elapsedSeconds, IReadOnlyList<Sprite> dynamicSprites)
    {
        Synchronize(sprites);
        var total = sprites.Count + dynamicSprites.Count;
        if (_frames.Length < total)
        {
            System.Array.Resize(ref _frames, Math.Max(total, _frames.Length * 2));
            System.Array.Resize(ref _widths, _frames.Length);
        }
        if (_outputBytes.Length < (total + 1) * 8)
        {
            _output?.Dispose();
            _outputBytes = new byte[(total + 1) * 16];
            _output = new Uint32Array(_outputBytes.Length / 4);
        }
        if (_dynamicBytes.Length < Math.Max(1, dynamicSprites.Count) * 16)
        {
            _dynamicUpload?.Dispose();
            _dynamicBytes = new byte[Math.Max(1, dynamicSprites.Count) * 32];
            _dynamicUpload = new Float32Array(_dynamicBytes.Length / 4);
        }
        var overrides = MemoryMarshal.Cast<byte, uint>(_widthBytes.AsSpan());
        var widthsChanged = !_widthsUploaded;
        for (var i = 0; i < _atlasIndices.Count; i++)
        {
            var index = _atlasIndices[i];
            var sprite = sprites[index];
            var frame = sprite.ViewFrame(position, elapsedSeconds);
            _frames[index] = frame;
            _widths[index] = sprite.ViewWidth(frame);
            var widthBits = BitConverter.SingleToUInt32Bits(_widths[index]);
            widthsChanged |= overrides[i * 2 + 1] != widthBits;
            overrides[i * 2] = (uint)index;
            overrides[i * 2 + 1] = widthBits;
        }
        var dynamicData = MemoryMarshal.Cast<byte, float>(_dynamicBytes.AsSpan());
        for (var i = 0; i < dynamicSprites.Count; i++)
        {
            var avatar = dynamicSprites[i];
            var index = sprites.Count + i;
            var frame = avatar.ViewFrame(position, elapsedSeconds);
            _frames[index] = frame;
            _widths[index] = avatar.ViewWidth(frame);
            dynamicData[i * 4] = avatar.Position.X;
            dynamicData[i * 4 + 1] = avatar.Position.Y;
            dynamicData[i * 4 + 2] = _widths[index];
            dynamicData[i * 4 + 3] = avatar.MaximumViewWidth;
        }
        if (widthsChanged) _widthOverrides!.WriteBytes(_widthBytes);
        _widthsUploaded = true;
        if (dynamicSprites.Count > 0) _dynamicUpload!.WriteBytes(_dynamicBytes);
        var count = module.JSRef!.CallApply<int>("select", new object[] { _output!, _widthOverrides!, _atlasIndices.Count,
            position.X, position.Y, range, direction.X, direction.Y, plane.Length(), cullView,
            _dynamicUpload!, dynamicSprites.Count });
        _output!.Read(0, _outputBytes, 0, (count + 1) * 8);
        selection.Resolve(sprites, MemoryMarshal.Cast<byte, uint>(_outputBytes.AsSpan()), count, _frames, _widths, dynamicSprites);
    }

    public void ReleaseScene()
    {
        module.JSRef!.CallVoid("release");
        _positions?.Dispose(); _widthOverrides?.Dispose(); _output?.Dispose();
        _dynamicUpload?.Dispose(); _dynamicUpload = null; _dynamicBytes = [];
        _positions = null; _widthOverrides = _output = null;
        _source = null;
        _widthsUploaded = false;
        _frames = []; _widths = []; _outputBytes = _widthBytes = [];
        _atlasIndices.Clear();
    }

    public void Dispose()
    {
        ReleaseScene();
        module.Dispose();
    }
}
