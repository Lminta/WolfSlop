using WolfNET.Engine.World;

namespace WolfNET.Engine.Audio;

/// <summary>Repeating music queue and crossfades. Owns its backend and two streams;
/// the host calls Update from its game loop. Contains no browser dependencies.</summary>
public sealed class MusicPlayer : IDisposable
{
    private sealed record Track(int Index, IAudioStream Stream);
    private readonly record struct Fade(double Start, double Duration);
    private readonly IAudioBackend _backend;
    private readonly string[] _tracks;
    private readonly double _crossfadeSeconds;
    private readonly float _volume;
    private readonly HashSet<int> _failed = [];
    private Track? _current, _next;
    private Fade? _fade;
    private bool _playing, _disposed;
    private float _volumeScale = 1;

    public MusicPlayer(IAudioBackend backend, MusicDefinition music, MusicSettings? sound = null)
    {
        music.Validate();
        sound ??= new();
        sound.Validate();
        _backend = backend;
        _crossfadeSeconds = music.CrossfadeSeconds ?? sound.MusicCrossfadeSeconds;
        _volume = sound.GetMusicVolume(music);
        _tracks = _volume > 0 ? (string[])music.Tracks.Clone() : [];
        backend.SetVolume(0);
        try
        {
            _current = Open(NextIndex(-1));
            _next = _current == null ? null : Open(NextIndex(_current.Index));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private int NextIndex(int after)
    {
        for (var offset = 1; offset <= _tracks.Length; offset++)
        {
            var index = (after + offset) % _tracks.Length;
            if (!_failed.Contains(index)) return index;
        }
        return -1;
    }

    private Track? Open(int index) => index < 0 ? null : new(index, _backend.OpenStream(_tracks[index]));

    public void SetPlaying(bool playing, float volumeScale = 1)
    {
        if (_disposed) return;
        if (_volume == 0) playing = false;
        if (!float.IsFinite(volumeScale) || volumeScale < 0 || volumeScale > 1)
            throw new ArgumentOutOfRangeException(nameof(volumeScale));
        var playbackChanged = _playing != playing;
        if (!playbackChanged && _volumeScale == volumeScale) return;
        _playing = playing;
        _volumeScale = volumeScale;
        _backend.SetVolume(playing ? _volume * volumeScale : 0);
        if (!playbackChanged) return;
        if (playing) _backend.Resume();
        else
        {
            _current?.Stream.Pause();
            _next?.Stream.Pause();
        }
    }

    private void Skip(int index)
    {
        _failed.Add(index);
        if (_next?.Index == index)
        {
            _next.Stream.Dispose();
            _next = null;
        }
        if (_current?.Index == index)
        {
            _current.Stream.Dispose();
            _current = _next;
            _next = null;
        }
        _fade = null;
        _current ??= Open(NextIndex(index));
        if (_current != null) _next ??= Open(NextIndex(_current.Index));
    }

    private void Promote()
    {
        _current?.Stream.Dispose();
        _current = _next;
        _fade = null;
        _current?.Stream.SetVolume(1);
        _next = _current == null ? null : Open(NextIndex(_current.Index));
    }

    public void Update()
    {
        if (_disposed) return;
        // Each failed queue entry is excluded for this session, so even an
        // entirely unavailable playlist terminates without an endless retry.
        _backend.Refresh();
        AudioStreamState current = default, next = default;
        while (_current != null)
        {
            current = _current.Stream.State;
            next = _next?.Stream.State ?? default;
            if (current.Failed) Skip(_current.Index);
            else if (next.Failed) Skip(_next!.Index);
            else break;
        }
        if (!_playing || _current == null) return;
        if (_fade is { } fade)
        {
            var progress = (float)Math.Clamp((next.PositionSeconds - fade.Start) / fade.Duration, 0, 1);
            if (progress >= 1 || current.Ended) Promote();
            else
            {
                _current.Stream.SetVolume(1 - progress, 0.1);
                _next!.Stream.SetVolume(progress, 0.1);
            }
            Resume(_current);
            if (_fade != null) Resume(_next);
            return;
        }
        _current.Stream.SetVolume(1);
        _next?.Stream.SetVolume(0);
        if (current.Ended)
        {
            Promote();
            Resume(_current);
            return;
        }
        Resume(_current, current);
        var remaining = current.DurationSeconds - current.PositionSeconds;
        var duration = Math.Min(_crossfadeSeconds, Math.Min(current.DurationSeconds / 2, next.DurationSeconds / 2));
        if (_next == null || !double.IsFinite(remaining) || !double.IsFinite(duration) ||
            duration <= 0 || remaining > duration || current.Starting) return;
        if (next.Paused) Resume(_next, next);
        else if (!next.Starting)
            _fade = new(next.PositionSeconds, Math.Max(0.01, Math.Min(duration, remaining)));
    }

    private static void Resume(Track? track, AudioStreamState? state = null)
    {
        if (track == null) return;
        var playback = state ?? track.Stream.State;
        if (playback.Paused && !playback.Ended && !playback.Starting) track.Stream.Play();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _current?.Stream.Dispose();
        _next?.Stream.Dispose();
        _current = _next = null;
        _backend.Dispose();
    }
}
