namespace WolfNET.Engine.Audio;

public record MusicSettings
{
    public float Volume { get; init; } = 1;
    public float MusicVolume { get; init; } = .65f;
    public float MusicCrossfadeSeconds { get; init; } = 3;

    public virtual void Validate()
    {
        if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1 ||
            !float.IsFinite(MusicVolume) || MusicVolume < 0 || MusicVolume > 1 ||
            !float.IsFinite(MusicCrossfadeSeconds) || MusicCrossfadeSeconds < 0)
            throw new InvalidDataException("Music volumes must be between zero and one; crossfade duration must be finite and nonnegative.");
    }

    public float GetMusicVolume(MusicDefinition music) => Volume * (music.Volume ?? MusicVolume);
}
