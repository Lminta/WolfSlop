using System.Numerics;
using System.Runtime.InteropServices;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;
using WolfNET.Engine.World;

namespace WolfNET.Engine.Browser.Graphics;

/// <summary>Retains scenery, atlas views, ordering, and the packed GPU upload in the browser.</summary>
internal sealed class BrowserSpriteSelection : IDisposable
{
    private readonly JSObject _module;
    public BrowserSpriteSelection(JSObject module, GPUQueue queue, GPUBuffer camera)
    {
        _module = module;
        module.JSRef!.CallVoid("setCamera", queue, camera);
    }
    private IReadOnlyList<Sprite>? _source;
    private Float32Array? _positions;
    private Uint32Array? _staticUpload;
    private byte[] _dynamicBytes = [];
    public int CandidatesExamined => _module.JSRef!.Call<int>("getCandidateCount");

    private void Synchronize(IReadOnlyList<Sprite> sprites)
    {
        if (ReferenceEquals(_source, sprites)) return;
        ReleaseScene();
        _source = sprites;
        var (records, atlases) = SpriteSelection.PrepareGpuScene(sprites);
        var bytes = new byte[Math.Max(1, sprites.Count) * 16];
        var positions = MemoryMarshal.Cast<byte, float>(bytes.AsSpan());
        for (var i = 0; i < sprites.Count; i++)
        {
            positions[i * 4] = sprites[i].Position.X;
            positions[i * 4 + 1] = sprites[i].Position.Y;
            positions[i * 4 + 2] = records[i].Scale.X;
            positions[i * 4 + 3] = sprites[i].MaximumViewWidth;
        }
        _positions = new Float32Array(positions.Length);
        _positions.WriteBytes(bytes);
        _staticUpload = new Uint32Array(Math.Max(1, records.Length) * 8);
        if (records.Length > 0) _staticUpload.WriteBytes(MemoryMarshal.AsBytes(records.AsSpan()).ToArray());
        _module.JSRef!.CallVoid("prepareGpu", _positions, _staticUpload, atlases, sprites.Count);
    }

    public int Select(IReadOnlyList<Sprite> sprites, Vector2 position, Vector2 direction,
        Vector2 plane, float range, bool cullView, double elapsedSeconds, IReadOnlyList<Sprite> dynamicSprites, byte[] cameraBytes,
        int columnStep = 0, uint workgroups = 0)
    {
        Synchronize(sprites);
        var needed = Math.Max(1, dynamicSprites.Count) * 48;
        if (_dynamicBytes.Length < needed)
        {
            _dynamicBytes = new byte[needed * 2];
        }
        var floats = MemoryMarshal.Cast<byte, float>(_dynamicBytes.AsSpan());
        var words = MemoryMarshal.Cast<byte, uint>(_dynamicBytes.AsSpan());
        for (var i = 0; i < dynamicSprites.Count; i++)
        {
            var sprite = dynamicSprites[i];
            var frame = sprite.ViewFrame(position, elapsedSeconds);
            var offset = i * 12;
            floats[offset] = sprite.Position.X; floats[offset + 1] = sprite.Position.Y;
            words[offset + 2] = sprite.TextureLayer + (uint)frame; words[offset + 3] = sprite.OwnerCell;
            floats[offset + 4] = sprite.ViewWidth(frame); floats[offset + 5] = sprite.Scale.Y;
            floats[offset + 6] = sprite.Elevation; words[offset + 7] = sprite.ViewKind(frame);
            floats[offset + 8] = sprite.MaximumViewWidth;
        }
        // A complemented count requests buffer growth before the owner uploads
        // and submits this already prepared frame.
        return _module.JSRef!.CallApply<int>(columnStep == 0 ? "selectFrame" : "renderFrame",
            new object[] { cameraBytes, _dynamicBytes, position.X, position.Y, range,
                direction.X, direction.Y, plane.Length(), cullView, elapsedSeconds, dynamicSprites.Count, columnStep, workgroups });
    }

    public void SetSubmission(JSObject submission, GPUBuffer buffer, GPUBuffer mapInfo, int capacity) =>
        _module.JSRef!.CallVoid("setSubmission", submission, buffer, mapInfo, capacity);

    public void Upload(GPUBuffer sprites, GPUBuffer mapInfo) =>
        _module.JSRef!.CallVoid("uploadFrame", sprites, mapInfo);

    public void ReleaseScene()
    {
        _module.JSRef!.CallVoid("release");
        _positions?.Dispose(); _staticUpload?.Dispose();
        _positions = null; _staticUpload = null;
        _dynamicBytes = [];
        _source = null;
    }

    public void ClearSubmission() => _module.JSRef!.CallVoid("clearSubmission");

    public void Dispose() { ReleaseScene(); _module.Dispose(); }
}
