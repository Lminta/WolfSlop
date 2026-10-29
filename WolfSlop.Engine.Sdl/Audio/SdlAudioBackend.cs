using System.Diagnostics;
using SDL3;
using WolfSlop.Engine.Assets;
using WolfSlop.Engine.Audio;
using WolfSlop.Engine.Sdl.Graphics;

namespace WolfSlop.Engine.Sdl.Audio;

public sealed class SdlAudioBackend : IAudioBackend
{
    internal nint MixerHandle { get; }
    internal DirectoryAssetSource Assets { get; }
    public SdlAudioBackend(DirectoryAssetSource assets)
    {
        Assets = assets; GpuDevice.Check(Mixer.Init());
        MixerHandle = GpuDevice.Require(Mixer.CreateMixerDevice(SDL.AudioDeviceDefaultPlayback, 0));
    }
    public IAudioStream OpenStream(string path) => new SdlAudioStream(this, path);
    public void SetVolume(float volume) => Mixer.SetMixerGain(MixerHandle, volume);
    public void Resume() { }
    public void Dispose() { Mixer.DestroyMixer(MixerHandle); Mixer.Quit(); }

    private sealed class SdlAudioStream : IAudioStream
    {
        private readonly nint _audio, _track;
        private bool _started, _failed;
        private readonly Stopwatch _rampClock = new();
        private float _gain = 1, _target = 1, _from = 1;
        private double _duration;
        public SdlAudioStream(SdlAudioBackend backend, string path)
        {
            _audio = Mixer.LoadAudio(backend.MixerHandle, backend.Assets.Resolve(path), false);
            if (_audio == 0) { _failed = true; return; }
            _track = Mixer.CreateTrack(backend.MixerHandle);
            _failed = _track == 0 || !Mixer.SetTrackAudio(_track, _audio);
        }
        public AudioStreamState State => new(
            _track == 0 ? 0 : Mixer.TrackFramesToMS(_track, Mixer.GetTrackPlaybackPosition(_track)) / 1000.0,
            _audio == 0 ? 0 : Mixer.AudioFramesToMS(_audio, Mixer.GetAudioDuration(_audio)) / 1000.0,
            !_started || Mixer.TrackPaused(_track), _started && !Mixer.TrackPlaying(_track), false, _failed);
        public void Play()
        {
            if (_failed) return;
            if (_started) Mixer.ResumeTrack(_track);
            else { _started = true; _failed = !Mixer.PlayTrack(_track, 0); }
        }
        public void Pause() { if (_track != 0) Mixer.PauseTrack(_track); }
        public void SetVolume(float volume, double rampSeconds = 0)
        {
            if (_track == 0) return;
            if (_rampClock.IsRunning)
            {
                _gain = float.Lerp(_from, _target, (float)Math.Clamp(_rampClock.Elapsed.TotalSeconds / _duration, 0, 1));
                if (_rampClock.Elapsed.TotalSeconds >= _duration) _rampClock.Stop();
            }
            if (rampSeconds <= 0) { _gain = _target = volume; _rampClock.Reset(); }
            else if (_target != volume) { _from = _gain; _target = volume; _duration = rampSeconds; _rampClock.Restart(); }
            Mixer.SetTrackGain(_track, _gain);
        }
        public void Dispose() { if (_track != 0) Mixer.DestroyTrack(_track); if (_audio != 0) Mixer.DestroyAudio(_audio); }
    }
}

public sealed class SdlSfxBackend<TCue> : ISfxBackend<TCue> where TCue : notnull
{
    private readonly SdlAudioBackend _backend;
    private readonly Dictionary<TCue, nint[]> _clips = [];
    private readonly HashSet<Voice> _voices = [];
    private readonly Dictionary<nint, float> _gains = [];
    private readonly Func<TCue, int, int, int>? _select;
    private bool _paused;
    public SdlSfxBackend(DirectoryAssetSource assets, IReadOnlyDictionary<TCue, string[]> clips, Func<TCue, int, int, int>? select = null,
        Func<TCue, string, float>? clipGain = null)
    {
        _backend = new(assets); _select = select;
        try
        {
            foreach (var (cue, paths) in clips)
            {
                var handles = new List<nint>(); _clips[cue] = [];
                foreach (var path in paths)
                {
                    var audio = GpuDevice.Require(Mixer.LoadAudio(_backend.MixerHandle, assets.Resolve(path), true));
                    handles.Add(audio); _clips[cue] = handles.ToArray();
                    _gains[audio] = clipGain?.Invoke(cue, path) ?? 1;
                }
            }
        }
        catch { Dispose(); throw; }
    }
    public ISfxVoice? Play(TCue cue, bool loop, float phase = 0, int emitterId = 0, int? variantIndex = null)
    {
        foreach (var voice in _voices.Where(v => v.Finished).ToArray()) voice.Dispose();
        if (!_clips.TryGetValue(cue, out var clips) || clips.Length == 0) return null;
        var index = variantIndex ?? _select?.Invoke(cue, clips.Length, emitterId) ?? Random.Shared.Next(clips.Length);
        var audio = clips[Math.Clamp(index, 0, clips.Length - 1)];
        var track = GpuDevice.Require(Mixer.CreateTrack(_backend.MixerHandle));
        var options = SDL.CreateProperties();
        try
        {
            GpuDevice.Check(Mixer.SetTrackAudio(track, audio));
            SDL.SetNumberProperty(options, Mixer.Props.PlayLoopsNumber, loop ? -1 : 0);
            SDL.SetNumberProperty(options, Mixer.Props.PlayStartFrameNumber, (long)(Math.Clamp(phase, 0, .999999f) * Mixer.GetAudioDuration(audio)));
            Mixer.SetTrackGain(track, 0);
            GpuDevice.Check(Mixer.PlayTrack(track, options));
            if (_paused) Mixer.PauseTrack(track);
            var voice = new Voice(this, track, _gains[audio]); _voices.Add(voice); return voice;
        }
        catch { Mixer.DestroyTrack(track); throw; }
        finally { SDL.DestroyProperties(options); }
    }
    public void SetPaused(bool paused)
    {
        if (_paused == paused) return; _paused = paused;
        if (paused) Mixer.PauseAllTracks(_backend.MixerHandle); else Mixer.ResumeAllTracks(_backend.MixerHandle);
    }
    public void Dispose()
    {
        foreach (var voice in _voices.ToArray()) voice.Dispose();
        foreach (var audio in _clips.Values.SelectMany(value => value)) Mixer.DestroyAudio(audio);
        _clips.Clear(); _backend.Dispose();
    }
    private sealed class Voice(SdlSfxBackend<TCue> owner, nint track, float clipGain) : ISfxVoice
    {
        private bool _disposed;
        public bool Finished => _disposed || !Mixer.TrackPlaying(track);
        public unsafe void SetMix(float volume, float pan, float pitch)
        {
            if (_disposed) return;
            Mixer.SetTrackGain(track, volume * clipGain); Mixer.SetTrackFrequencyRatio(track, pitch);
            var gains = new Mixer.StereoGains { Left = MathF.Sqrt((1 - pan) * .5f), Right = MathF.Sqrt((1 + pan) * .5f) };
            Mixer.SetTrackStereo(track, (nint)(&gains));
        }
        public void Dispose() { if (_disposed) return; _disposed = true; Mixer.DestroyTrack(track); owner._voices.Remove(this); }
    }
}
