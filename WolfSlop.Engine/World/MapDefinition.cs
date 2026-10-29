using System.Numerics;

namespace WolfSlop.Engine.World;

public sealed record MapDefinition
{
    // Object ID to JSON path, relative to this map file.
    public Dictionary<int, string> Objects { get; init; } = [];
    // Each number identifies a definition from the object registry, including ID zero.
    public required int[][] WorldMap { get; init; }
    public ActorStartDefinition? Spawn { get; init; }
    public SkyDefinition? Sky { get; init; }
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement> Metadata { get; init; } = [];
    public MusicDefinition? Music { get; init; }
}

public sealed record MusicDefinition
{
    // Asset paths relative to wwwroot, in repeating playback order.
    public string[] Tracks { get; init; } = [];
    // Optional per-map overrides; otherwise audio-settings.json supplies defaults.
    public float? CrossfadeSeconds { get; init; }
    public float? Volume { get; init; }

    public void Validate()
    {
        if (Tracks == null || Tracks.Any(string.IsNullOrWhiteSpace) ||
            CrossfadeSeconds is { } fade && (!float.IsFinite(fade) || fade < 0) ||
            Volume is { } volume && (!float.IsFinite(volume) || volume < 0 || volume > 1))
            throw new InvalidDataException("Music needs nonempty track paths, a finite nonnegative crossfadeSeconds, and volume between zero and one.");
    }
}

public sealed record SkyDefinition
{
    public required string Texture { get; init; }
    public float RotationDegrees { get; init; }
}

public sealed record ActorStartDefinition
{
    public string Controller { get; init; } = "player";
    public required float X { get; init; }
    public required float Y { get; init; }
    public required float DirectionX { get; init; }
    public required float DirectionY { get; init; }
    // Squares per second and maximum radians per second, respectively.
    public required float MoveSpeed { get; init; }
    public required float RotSpeed { get; init; }
    public float CollisionRadius { get; init; } = 0.15f;
    public float JumpRise { get; init; } = 0.4f;
    // Horizontal travel in map units; zero disables jumping.
    public float JumpLength { get; init; } = 2f;
    public float JumpSpeedMultiplier { get; init; } = 1.25f;
    public float CrouchDrop { get; init; } = 0.25f;
    public float CrouchSpeedMultiplier { get; init; } = 0.5f;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Controller))
            throw new InvalidDataException("Controller must have a name.");
        var direction = new Vector2(DirectionX, DirectionY);
        if (!float.IsFinite(CrouchDrop) || CrouchDrop <= 0 || CrouchDrop >= 0.5f ||
            !float.IsFinite(CrouchSpeedMultiplier) || CrouchSpeedMultiplier <= 0 ||
            !float.IsFinite(MoveSpeed * CrouchSpeedMultiplier))
            throw new InvalidDataException("crouchDrop must be greater than 0 and less than 0.5; crouchSpeedMultiplier must be positive and produce a finite speed.");
        if (!float.IsFinite(JumpRise) || JumpRise < 0 || JumpRise >= 0.5f ||
            !float.IsFinite(JumpLength) || JumpLength < 0 || JumpLength > 64 ||
            !float.IsFinite(JumpSpeedMultiplier) || JumpSpeedMultiplier <= 0 ||
            !float.IsFinite(MoveSpeed * JumpSpeedMultiplier))
            throw new InvalidDataException("jumpRise must be in [0, 0.5), jumpLength in [0, 64], and jumpSpeedMultiplier positive; all jump settings and airborne speed must be finite.");
        if (JumpLength > 0 && MoveSpeed > 0)
        {
            var flightTime = JumpLength / (MoveSpeed * JumpSpeedMultiplier);
            if (!float.IsFinite(flightTime) || flightTime <= 0)
                throw new InvalidDataException("Jump settings must produce a positive, finite flight time.");
        }
        if (!float.IsFinite(CollisionRadius) || CollisionRadius <= 0 || CollisionRadius >= 0.5f)
            throw new InvalidDataException("Actor collisionRadius must be finite, greater than zero, and less than 0.5.");
        if (!float.IsFinite(MoveSpeed) || MoveSpeed < 0 ||
            !float.IsFinite(RotSpeed) || RotSpeed < 0)
            throw new InvalidDataException("Actor speeds must be finite and nonnegative.");
        if (!float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() <= 0)
            throw new InvalidDataException("Actor spawn direction must be finite and nonzero.");
    }
}
