using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

using WolfSlop.Engine.Audio;

namespace WolfSlop.Engine.Browser.Audio;

/// <summary>Browser streaming and mixing primitives. Queue policy belongs to MusicPlayer.</summary>
public sealed class BrowserAudioBackend : IAudioBackend
{
    private readonly HTMLCanvasElement _canvas;
    private readonly AudioContext _context = new();
    private readonly GainNode _output;
    private readonly AudioParam _volume;
    private readonly List<BrowserAudioStream> _streams = [];
    private bool _disposed;

    public BrowserAudioBackend(HTMLCanvasElement canvas)
    {
        _canvas = canvas;
        _output = _context.CreateGain();
        _volume = _output.Gain;
        _volume.Value = 0;
        using var destination = _context.Destination;
        _output.Connect(destination).Dispose();
        _canvas.OnClick += OnClick;
    }

    public IAudioStream OpenStream(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var stream = new BrowserAudioStream(this, path);
        _streams.Add(stream);
        return stream;
    }

    public void SetVolume(float volume) => _volume.Value = volume;

    public void Resume() => _ = ResumeAsync();

    private async Task ResumeAsync()
    {
        using var context = _context.JSRefCopy<AudioContext>();
        try { await context.Resume(); }
        catch (Exception ex) { if (!_disposed) Console.Error.WriteLine($"[AUDIO] Resume failed: {ex.Message}"); }
    }

    private void OnClick(MouseEvent e)
    {
        // These calls execute before the event handler yields, preserving user
        // activation. Priming is silent and preserves the paused stream position.
        Resume();
        foreach (var stream in _streams) stream.Unlock();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _canvas.OnClick -= OnClick;
        foreach (var stream in _streams.ToArray()) stream.Dispose();
        _volume.Dispose();
        _output.Disconnect();
        _output.Dispose();
        _ = CloseAsync();
        _context.Dispose();
    }

    private async Task CloseAsync()
    {
        using var context = _context.JSRefCopy<AudioContext>();
        try { await context.Close(); }
        catch (Exception ex) { Console.Error.WriteLine($"[AUDIO] Close failed: {ex.Message}"); }
    }

    private sealed class BrowserAudioStream : IAudioStream
    {
        private readonly BrowserAudioBackend _owner;
        private readonly HTMLAudioElement _audio;
        private readonly MediaElementAudioSourceNode _source;
        private readonly GainNode _gainNode;
        private readonly AudioParam _gain;
        private float _requestedVolume = float.NaN;
        private double _requestedRamp = double.NaN;
        private bool _desiredPlaying, _starting, _blocked, _failed, _disposed;

        public BrowserAudioStream(BrowserAudioBackend owner, string path)
        {
            _owner = owner;
            _audio = new HTMLAudioElement("");
            _audio.SetAttribute("preload", "auto");
            _source = owner._context.CreateMediaElementSource(_audio);
            _gainNode = owner._context.CreateGain();
            _gain = _gainNode.Gain;
            _gain.Value = 0;
            _source.Connect(_gainNode).Dispose();
            _gainNode.Connect(owner._output).Dispose();
            _audio.OnError += OnError;
            _audio.Src = path;
        }

        public AudioStreamState State => new(_audio.CurrentTime, _audio.Duration ?? double.NaN,
            _audio.Paused, _audio.Ended, _starting, _failed);

        private void OnError(Event e)
        {
            _failed = true;
            var error = _audio.Error;
            Console.Error.WriteLine($"[AUDIO] Stream failed: {error?.Message}");
        }

        public void Play()
        {
            _desiredPlaying = true;
            if (!_disposed && !_starting && !_blocked && !_failed) _ = StartAsync(false);
        }

        public void Pause()
        {
            _desiredPlaying = false;
            if (!_disposed) _audio.Pause();
        }

        public void Unlock()
        {
            _blocked = false;
            if (!_disposed && !_starting && !_failed) _ = StartAsync(!_desiredPlaying);
        }

        private async Task StartAsync(bool prime)
        {
            _starting = true;
            // Keep the DOM reference alive if a map change releases this stream
            // while play() is still waiting for data or browser authorization.
            using var audio = _audio.JSRefCopy<HTMLAudioElement>();
            var position = audio.CurrentTime;
            try
            {
                await audio.Play();
                if (_disposed) return;
                if (!_desiredPlaying)
                {
                    audio.Pause();
                    if (prime) audio.CurrentTime = position;
                }
            }
            catch (JSException ex) when (ex.Name is "NotAllowedError" or "AbortError")
            {
                if (!_disposed && ex.Name == "NotAllowedError") _blocked = true;
            }
            catch (Exception ex)
            {
                if (!_disposed)
                {
                    _failed = true;
                    Console.Error.WriteLine($"[AUDIO] Playback failed: {ex.Message}");
                }
            }
            finally { _starting = false; }
        }

        public void SetVolume(float volume, double rampSeconds = 0)
        {
            if (_requestedVolume == volume && _requestedRamp == rampSeconds) return;
            var now = _owner._context.CurrentTime;
            _gain.CancelScheduledValues(now).Dispose();
            _gain.SetValueAtTime(_gain.Value, now).Dispose();
            if (rampSeconds > 0) _gain.LinearRampToValueAtTime(volume, now + rampSeconds).Dispose();
            else _gain.SetValueAtTime(volume, now).Dispose();
            _requestedVolume = volume;
            _requestedRamp = rampSeconds;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _audio.OnError -= OnError;
            _audio.Pause();
            _audio.RemoveAttribute("src");
            _audio.Load();
            _source.Disconnect();
            _gainNode.Disconnect();
            _gain.Dispose();
            _gainNode.Dispose();
            _source.Dispose();
            _audio.Dispose();
            _owner._streams.Remove(this);
        }
    }
}
