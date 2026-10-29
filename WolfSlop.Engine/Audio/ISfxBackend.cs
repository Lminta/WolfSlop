namespace WolfSlop.Engine.Audio;

/// <summary>Decoded clips keyed by an application's own cue type.</summary>
public interface ISfxBackend<TCue> : IDisposable where TCue : notnull
{
    ISfxVoice? Play(TCue cue, bool loop, float phase = 0, int emitterId = 0, int? variantIndex = null);
    void SetPaused(bool paused);
    void Commit() { }
}

public interface ISfxVoice : IDisposable
{
    bool Finished { get; }
    void SetMix(float volume, float pan, float pitch);
}
