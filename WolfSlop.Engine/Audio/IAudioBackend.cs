namespace WolfSlop.Engine.Audio;

/// <summary>Playback primitives supplied by the host, independent of the music queue.</summary>
public interface IAudioBackend : IDisposable
{
    IAudioStream OpenStream(string path);
    void SetVolume(float volume);
    void Resume();
}

public readonly record struct AudioStreamState(double PositionSeconds, double DurationSeconds,
    bool Paused, bool Ended, bool Starting, bool Failed);

/// <summary>A streamed file. File/decoder failures are reported through State.</summary>
public interface IAudioStream : IDisposable
{
    AudioStreamState State { get; }
    void Play();
    void Pause();
    void SetVolume(float volume, double rampSeconds = 0);
}
