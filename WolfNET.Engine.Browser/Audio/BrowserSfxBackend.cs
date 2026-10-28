using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

using WolfNET.Engine.Audio;

namespace WolfNET.Engine.Browser.Audio;

/// <summary>Decoded clips with C# mixing policy and batched Web Audio parameter writes.</summary>
public sealed class BrowserSfxBackend<TCue> : ISfxBackend<TCue> where TCue : notnull
{
    private readonly AudioContext _context = new();
    private readonly HTMLCanvasElement _canvas;
    private readonly GainNode _output;
    private readonly AudioParam _volume;
    private readonly Dictionary<TCue, List<AudioBuffer>> _clips = [];
    private readonly Dictionary<(TCue, AudioBuffer), float> _clipGains = [];
    private readonly Func<TCue, int, int, int> _selectVariant;
    private readonly Func<TCue, float[][], float>? _clipGain;
    private readonly Func<TCue, bool>? _analyzeClip;
    private readonly List<Voice> _voices = [];
    private readonly JSObject _mixer;
    private readonly Stack<int> _freeVoiceIds = [];
    private int _nextVoiceId;
    private byte[] _mixBytes = new byte[32 * 4 * sizeof(float)];
    private Float32Array _mixUpload;
    private bool _paused = true, _disposed;

    private BrowserSfxBackend(HTMLCanvasElement canvas, JSObject module, Func<TCue, int, int, int>? selectVariant,
        Func<TCue, float[][], float>? clipGain, Func<TCue, bool>? analyzeClip)
    {
        _canvas = canvas;
        _selectVariant = selectVariant ?? ((_, count, _) => Random.Shared.Next(count));
        _clipGain = clipGain;
        _analyzeClip = analyzeClip;
        _output = _context.CreateGain();
        _volume = _output.Gain;
        _volume.Value = 0;
        using var destination = _context.Destination;
        _output.Connect(destination).Dispose();
        _mixer = module.JSRef!.Call<JSObject>("createMixer", _context);
        _mixUpload = new Float32Array(_mixBytes.Length / sizeof(float));
        _canvas.OnClick += OnClick;
    }

    public static async Task<BrowserSfxBackend<TCue>> CreateAsync(HttpClient http, BlazorJSRuntime js, HTMLCanvasElement canvas,
        IReadOnlyDictionary<TCue, string[]> clips, CancellationToken cancellationToken = default,
        Func<TCue, int, int, int>? selectVariant = null, Func<TCue, float[][], float>? clipGain = null,
        Func<TCue, bool>? analyzeClip = null)
    {
        using var module = await js.Import<JSObject>("./_content/WolfNET.Engine.Browser/sfx-mixer.js");
        cancellationToken.ThrowIfCancellationRequested();
        var backend = new BrowserSfxBackend<TCue>(canvas, module, selectVariant, clipGain, analyzeClip);
        try
        {
            // Decode each unique file once, even if several cues share it.
            var decoded = new Dictionary<string, AudioBuffer?>();
            foreach (var (cue, files) in clips)
            {
                var buffers = new List<AudioBuffer>();
                backend._clips[cue] = buffers;
                foreach (var path in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!decoded.TryGetValue(path, out var buffer))
                    {
                        try
                        {
                            var bytes = await http.GetByteArrayAsync(path, cancellationToken);
                            using var data = (ArrayBuffer)bytes;
                            buffer = await backend._context.DecodeAudioData(data);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[SFX] Clip unavailable: {path}. {ex.Message}");
                            buffer = null;
                        }
                        decoded[path] = buffer;
                    }
                    if (buffer != null)
                    {
                        buffers.Add(buffer);
                        if (backend._clipGain != null && (backend._analyzeClip?.Invoke(cue) ?? true) &&
                            !backend._clipGains.ContainsKey((cue, buffer)))
                        {
                            var channels = new float[buffer.NumberOfChannels][];
                            for (var channelIndex = 0; channelIndex < channels.Length; channelIndex++)
                            {
                                using var channel = buffer.GetChannelData(channelIndex);
                                channels[channelIndex] = channel.ToArray();
                            }
                            backend._clipGains[(cue, buffer)] = backend._clipGain(cue, channels);
                        }
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return backend;
        }
        catch { backend.Dispose(); throw; }
    }

    public ISfxVoice? Play(TCue cue, bool loop, float phase = 0, int emitterId = 0, int? variantIndex = null)
    {
        if (_disposed || !_clips.TryGetValue(cue, out var clips) || clips.Count == 0) return null;
        var selected = variantIndex ?? _selectVariant(cue, clips.Count, emitterId);
        var index = (int)(Math.Abs((long)selected) % clips.Count);
        var gain = _clipGains.GetValueOrDefault((cue, clips[index]), 1);
        var voice = new Voice(this, clips[index], loop, phase, gain);
        _voices.Add(voice);
        return voice;
    }

    private void OnClick(MouseEvent e) => _ = ChangeStateAsync(false);

    public void Commit()
    {
        if (_disposed) return;
        if (_mixBytes.Length < _voices.Count * 4 * sizeof(float))
        {
            _mixBytes = new byte[_voices.Count * 8 * sizeof(float)];
            _mixUpload.Dispose();
            _mixUpload = new Float32Array(_mixBytes.Length / sizeof(float));
        }
        var values = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(_mixBytes.AsSpan());
        var count = 0;
        foreach (var voice in _voices)
            if (voice.WriteMix(values.Slice(count * 4, 4))) count++;
        if (count == 0) return;
        _mixUpload.WriteBytes(_mixBytes);
        _mixer.JSRef!.CallVoid("apply", _mixUpload, count);
    }

    public void SetPaused(bool paused)
    {
        if (_disposed || _paused == paused) return;
        _paused = paused;
        _volume.Value = paused ? 0 : 1;
        _ = ChangeStateAsync(paused);
    }

    private async Task ChangeStateAsync(bool paused)
    {
        using var context = _context.JSRefCopy<AudioContext>();
        try
        {
            if (paused) await context.Suspend();
            else await context.Resume();
        }
        catch (Exception ex) { if (!_disposed) Console.WriteLine($"[SFX] Audio activation: {ex.Message}"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _canvas.OnClick -= OnClick;
        foreach (var voice in _voices.ToArray()) voice.Dispose();
        _mixUpload.Dispose();
        _mixer.Dispose();
        foreach (var buffer in _clips.Values.SelectMany(clips => clips).Distinct()) buffer.Dispose();
        _clips.Clear();
        _clipGains.Clear();
        _volume.Dispose(); _output.Disconnect(); _output.Dispose();
        _ = CloseAsync();
        _context.Dispose();
    }

    private async Task CloseAsync()
    {
        using var context = _context.JSRefCopy<AudioContext>();
        try { await context.Close(); }
        catch (Exception ex) { Console.WriteLine($"[SFX] Audio close: {ex.Message}"); }
    }

    private sealed class Voice : ISfxVoice
    {
        private readonly BrowserSfxBackend<TCue> _owner;
        private readonly AudioBufferSourceNode _source;
        private readonly GainNode _gain;
        private readonly StereoPannerNode _panner;
        private readonly AudioParam _volume, _pan, _pitch;
        private readonly float _clipGain;
        private readonly int _id;
        private bool _disposed;
        private bool _dirty;
        private float _lastVolume = float.NaN, _lastPan = float.NaN, _lastPitch = float.NaN;
        public bool Finished { get; private set; }

        public Voice(BrowserSfxBackend<TCue> owner, AudioBuffer clip, bool loop, float phase, float clipGain)
        {
            _owner = owner;
            _id = owner._freeVoiceIds.Count > 0 ? owner._freeVoiceIds.Pop() : owner._nextVoiceId++;
            _clipGain = clipGain;
            _source = owner._context.CreateBufferSource();
            _source.Buffer = clip;
            _source.Loop = loop;
            _gain = owner._context.CreateGain();
            // Generated assets can be stereo. Downmix each positional source
            // before panning, preserving the original files on disk.
            _gain.ChannelCount = 1;
            _gain.ChannelCountMode = "explicit";
            _panner = owner._context.CreateStereoPanner();
            _volume = _gain.Gain; _pan = _panner.Pan;
            _pitch = _source.PlaybackRate ?? throw new InvalidOperationException("Audio source has no playback rate.");
            _volume.Value = 0;
            owner._mixer.JSRef!.CallVoid("add", _id, _volume, _pan, _pitch);
            _source.Connect(_gain).Dispose();
            _gain.Connect(_panner).Dispose();
            _panner.Connect(owner._output).Dispose();
            _source.OnEnded += OnEnded;
            _source.Start(0, loop ? (float)(clip.Duration * Math.Clamp(phase, 0, .999f)) : 0);
        }

        private void OnEnded(Event e) => Finished = true;

        public void SetMix(float volume, float pan, float pitch)
        {
            if (_disposed) return;
            void Stage(float value, ref float previous)
            {
                if (MathF.Abs(previous - value) < .0001f) return;
                previous = value;
                _dirty = true;
            }
            Stage(Math.Clamp(volume, 0, 1) * _clipGain, ref _lastVolume);
            Stage(Math.Clamp(pan, -1, 1), ref _lastPan);
            Stage(Math.Clamp(pitch, .5f, 2.5f), ref _lastPitch);
        }

        public bool WriteMix(Span<float> values)
        {
            if (!_dirty || _disposed) return false;
            values[0] = _id;
            values[1] = _lastVolume;
            values[2] = _lastPan;
            values[3] = _lastPitch;
            _dirty = false;
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner._mixer.JSRef!.CallVoid("remove", _id);
            _owner._freeVoiceIds.Push(_id);
            _source.OnEnded -= OnEnded;
            _source.Stop(0d);
            _source.Disconnect(); _gain.Disconnect(); _panner.Disconnect();
            _volume.Dispose(); _pan.Dispose(); _pitch.Dispose();
            _source.Dispose(); _gain.Dispose(); _panner.Dispose();
            _owner._voices.Remove(this);
        }
    }
}
