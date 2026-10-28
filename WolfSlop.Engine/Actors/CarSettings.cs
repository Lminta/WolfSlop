namespace WolfNET.Engine.Actors;

public sealed record CarSurfaceSettings
{
    public float MaxForwardSpeed { get; init; } = 25;
    public float MaxReverseSpeed { get; init; } = 4;
    public float Acceleration { get; init; } = 8;
    public float ReverseAcceleration { get; init; } = 4;
    public float BrakeDeceleration { get; init; } = 12;
    public float RollingResistance { get; init; } = .35f;
    public float LateralGrip { get; init; } = 9;
    public float MaxLateralAcceleration { get; init; } = 14;
    public float SurfacePenaltyCoefficient { get; init; } = 2;

    public void Validate()
    {
        foreach (var value in new[] { MaxForwardSpeed, MaxReverseSpeed, Acceleration, ReverseAcceleration,
            BrakeDeceleration, RollingResistance, LateralGrip, MaxLateralAcceleration, SurfacePenaltyCoefficient })
            if (!float.IsFinite(value) || value <= 0)
                throw new InvalidDataException("Car surface settings must be finite and positive.");
        if (MaxReverseSpeed >= MaxForwardSpeed)
            throw new InvalidDataException("Car reverse speed must be lower than forward speed.");
    }
}

public sealed record CarSettings
{
    // Surface identifiers treated as the primary traction profile. All others use OffRoad.
    public string[] RoadSurfaces { get; init; } = ["asphalt"];
    public const float MetersPerBlock = 3;
    // Sports coupe: 4.5 m long, 1.9 m wide, 1.2 m high, with a 2.7 m wheelbase.
    public float Length { get; init; } = 4.5f / MetersPerBlock;
    public float Width { get; init; } = 1.9f / MetersPerBlock;
    public float Height { get; init; } = 1.2f / MetersPerBlock;
    public float Wheelbase { get; init; } = 2.7f / MetersPerBlock;
    public float StopSpeed { get; init; } = .05f;
    public float MaxSteeringDegrees { get; init; } = 10;
    public float SteeringRateDegrees { get; init; } = 40;
    public float SteeringReturnDegrees { get; init; } = 80;
    public float AngularResponse { get; init; } = 5;
    public float CameraFollowDistance { get; init; } = 1.5f;
    public float CameraElevation { get; init; } = .25f;
    public float MouseSensitivity { get; init; } = .0025f;
    public float CameraRecenterDelay { get; init; } = 1;
    public float CameraRecenterRate { get; init; } = 4;
    public float CameraMaxPitchDegrees { get; init; } = 30;
    public float PartialMudPenalty { get; init; } = .5f;
    // Radians/second of side pull at the asphalt forward speed limit.
    public float MudPullStrength { get; init; } = .65f;
    public float SteeringWheelDegrees { get; init; } = 180;
    public CarSurfaceSettings Asphalt { get; init; } = new();
    public CarSurfaceSettings OffRoad { get; init; } = new()
    {
        MaxForwardSpeed = 8, MaxReverseSpeed = 2, Acceleration = 2.4f, ReverseAcceleration = 1.2f,
        BrakeDeceleration = 6, RollingResistance = .2f, LateralGrip = 3, MaxLateralAcceleration = 5
    };

    public void Validate()
    {
        if (RoadSurfaces == null || RoadSurfaces.Length == 0 || RoadSurfaces.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Car roadSurfaces must contain nonempty surface identifiers.");
        foreach (var value in new[] { Length, Width, Height, Wheelbase, StopSpeed, MaxSteeringDegrees,
            SteeringRateDegrees, SteeringReturnDegrees, AngularResponse, CameraFollowDistance,
            MouseSensitivity, CameraRecenterRate, CameraMaxPitchDegrees, SteeringWheelDegrees })
            if (!float.IsFinite(value) || value <= 0)
                throw new InvalidDataException("Car dimensions, steering and camera rates must be finite and positive.");
        if (Width >= 1 || Length > 4 || Wheelbase > Length || MaxSteeringDegrees >= 85 ||
            CameraMaxPitchDegrees >= 85 || !float.IsFinite(CameraElevation) ||
            CameraElevation < -.45f || CameraElevation > .45f || !float.IsFinite(CameraRecenterDelay) || CameraRecenterDelay < 0)
            throw new InvalidDataException("Car dimensions, steering or camera limits are out of range.");
        if (!float.IsFinite(PartialMudPenalty) || PartialMudPenalty < 0 || PartialMudPenalty > 1 ||
            !float.IsFinite(MudPullStrength) || MudPullStrength < 0)
            throw new InvalidDataException("partialMudPenalty must be in [0, 1] and mudPullStrength must be finite and nonnegative.");
        if (Asphalt == null || OffRoad == null) throw new InvalidDataException("Car needs asphalt and offRoad profiles.");
        Asphalt.Validate();
        OffRoad.Validate();
    }
}
