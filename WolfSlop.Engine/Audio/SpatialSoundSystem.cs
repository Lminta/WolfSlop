using System.Numerics;

namespace WolfSlop.Engine.Audio;

public readonly record struct SoundEmitter<TCue>(int Id, TCue Cue, Vector2 Position,
    float Volume = 1, float Pitch = 1, float Phase = 0) where TCue : notnull;

/// <summary>Bounded positional loops and one-shots using application-defined cues.
/// Update supplies the desired loops; absent or inaudible loops release their voices.</summary>
public sealed class SpatialSoundSystem<TCue> : IDisposable where TCue : notnull
{
    private readonly ISfxBackend<TCue> _backend;
    private readonly float _referenceDistance, _maxDistance;
    private readonly int _maxVoices;
    private readonly Dictionary<(int, TCue), ISfxVoice> _loops = [];
    private readonly List<(ISfxVoice Voice, SoundEmitter<TCue> Emitter)> _shots = [];
    private readonly HashSet<(int, TCue)> _wanted = [];
    private bool _disposed;
    private Vector2 _listener, _forward = Vector2.UnitX;

    public SpatialSoundSystem(ISfxBackend<TCue> backend, float referenceDistance = 2,
        float maxDistance = 45, int maxVoices = 32)
    {
        if (!float.IsFinite(referenceDistance) || referenceDistance <= 0 ||
            !float.IsFinite(maxDistance) || maxDistance <= referenceDistance || maxVoices <= 0)
            throw new ArgumentOutOfRangeException(nameof(referenceDistance));
        _backend = backend;
        _referenceDistance = referenceDistance; _maxDistance = maxDistance; _maxVoices = maxVoices;
    }

    private bool Audible(SoundEmitter<TCue> emitter) => emitter.Volume > 0 &&
        Vector2.DistanceSquared(_listener, emitter.Position) < _maxDistance * _maxDistance;

    private void Mix(ISfxVoice voice, SoundEmitter<TCue> emitter)
    {
        var (gain, pan) = SpatialAudio.Mix(_listener, _forward, emitter.Position, _referenceDistance, _maxDistance);
        voice.SetMix(emitter.Volume * gain, pan, emitter.Pitch);
    }

    public void Update(Vector2 listener, Vector2 forward, IEnumerable<SoundEmitter<TCue>> loops, bool active = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _listener = listener; _forward = forward;
        _backend.SetPaused(!active);
        if (!active) return;
        for (var i = _shots.Count - 1; i >= 0; i--)
        {
            var shot = _shots[i];
            if (shot.Voice.Finished) { shot.Voice.Dispose(); _shots.RemoveAt(i); }
            else Mix(shot.Voice, shot.Emitter);
        }
        _wanted.Clear();
        var desired = loops.Where(Audible).DistinctBy(emitter => (emitter.Id, emitter.Cue)).ToArray();
        foreach (var emitter in desired) _wanted.Add((emitter.Id, emitter.Cue));
        foreach (var key in _loops.Keys.Where(key => !_wanted.Contains(key)).ToArray())
        {
            _loops[key].Dispose(); _loops.Remove(key);
        }
        foreach (var emitter in desired)
        {
            var key = (emitter.Id, emitter.Cue);
            if (!_loops.TryGetValue(key, out var voice))
            {
                if (_loops.Count + _shots.Count >= _maxVoices) continue;
                voice = _backend.Play(emitter.Cue, true, emitter.Phase, emitter.Id);
                if (voice == null) continue;
                _loops.Add(key, voice);
            }
            Mix(voice, emitter);
        }
        _backend.Commit();
    }

    public bool Play(SoundEmitter<TCue> emitter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Audible(emitter) || _loops.Count + _shots.Count >= _maxVoices) return false;
        var voice = _backend.Play(emitter.Cue, false, emitter.Phase, emitter.Id);
        if (voice == null) return false;
        _shots.Add((voice, emitter));
        Mix(voice, emitter);
        _backend.Commit();
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var voice in _loops.Values) voice.Dispose();
        foreach (var shot in _shots) shot.Voice.Dispose();
        _loops.Clear(); _shots.Clear();
        _backend.Dispose();
    }
}
