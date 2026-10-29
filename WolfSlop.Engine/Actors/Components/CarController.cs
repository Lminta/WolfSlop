using System.Numerics;
using WolfSlop.Engine.Core;
using WolfSlop.Engine.Input;
using WolfSlop.Engine.Physics;

namespace WolfSlop.Engine.Actors.Components;

/// <summary>Fixed-step, surface-aware car movement. One map cell is one world unit.</summary>
public sealed class CarController : ComponentBase
{
    private const double StepSeconds = 1d / 120;
    private readonly IKeyboardInput _input;
    private readonly Func<Vector2, string> _surfaceAt;
    private readonly CarSettings _settings;
    private readonly OrientedBoxCollider _collider;
    private readonly OrientedBoxCollider _halfFootprint;
    private readonly CarSurfaceSettings _mixedSurface;
    private double _accumulator;

    public Vector2 Velocity { get; private set; }
    public float SteeringAngle { get; private set; }
    public float AngularVelocity { get; private set; }
    public float Speed => Velocity.Length();
    public float SignedSpeed => Vector2.Dot(Velocity, Parent.Transform.Rotation);
    public float Throttle { get; private set; }
    public float Braking { get; private set; }
    public float Acceleration { get; private set; }
    public bool OnMud { get; private set; }
    public float MudShakeStrength { get; private set; }
    public event Action<BoxImpact>? WorldImpact;

    public CarController(ObjectBase parent, IKeyboardInput input, INavigationMap navigation,
        Func<Vector2, string> surfaceAt, CarSettings settings) : base(parent)
    {
        _input = input;
        _surfaceAt = surfaceAt;
        _settings = settings;
        _collider = new OrientedBoxCollider(navigation, settings.Length, settings.Width);
        _halfFootprint = new OrientedBoxCollider(navigation, settings.Length, settings.Width * .5f);
        var asphalt = settings.Asphalt;
        var mud = settings.OffRoad;
        var amount = settings.PartialMudPenalty;
        float Blend(float road, float offRoad) => road + (offRoad - road) * amount;
        _mixedSurface = new CarSurfaceSettings
        {
            MaxForwardSpeed = Blend(asphalt.MaxForwardSpeed, mud.MaxForwardSpeed),
            MaxReverseSpeed = Blend(asphalt.MaxReverseSpeed, mud.MaxReverseSpeed),
            Acceleration = Blend(asphalt.Acceleration, mud.Acceleration),
            ReverseAcceleration = Blend(asphalt.ReverseAcceleration, mud.ReverseAcceleration),
            BrakeDeceleration = Blend(asphalt.BrakeDeceleration, mud.BrakeDeceleration),
            RollingResistance = Blend(asphalt.RollingResistance, mud.RollingResistance),
            LateralGrip = Blend(asphalt.LateralGrip, mud.LateralGrip),
            MaxLateralAcceleration = Blend(asphalt.MaxLateralAcceleration, mud.MaxLateralAcceleration),
            SurfacePenaltyCoefficient = mud.SurfacePenaltyCoefficient * amount
        };
    }

    // Pausing discards unfinished time while preserving all physical state.
    public void ResetTiming() => _accumulator = 0;

    public void ResolveCarContact(Vector2 position, Vector2 velocity, bool resetAngularVelocity = true)
    {
        Parent.Transform = new Transform { Position = position, Rotation = Parent.Transform.Rotation };
        Velocity = velocity;
        if (resetAngularVelocity) AngularVelocity = 0;
    }

    public override void Update(float deltaTime)
    {
        _input.ConsumeJump();
        if (!float.IsFinite(deltaTime) || deltaTime <= 0) return;
        // A suspended host frame must not fast-forward the car through the course.
        _accumulator += Math.Min(deltaTime / 1000d, .25);
        var keys = _input.KeyState;
        while (_accumulator + 1e-7 >= StepSeconds)
        {
            Step((float)StepSeconds, keys);
            _accumulator = Math.Max(0, _accumulator - StepSeconds);
        }
    }

    private void Step(float seconds, KeyState keys)
    {
        var transform = Parent.Transform;
        var previousVelocity = Velocity;
        var forward = Vector2.Normalize(transform.Rotation);
        var right = new Vector2(forward.Y, -forward.X);
        var (surface, pullSide) = SurfaceContact(transform.Position, forward, right);
        var steeringInput = _input is ISteeringInput analog ? Math.Clamp(analog.Steering, -1, 1) :
            (keys.StrafeLeft || keys.Left ? 1 : 0) - (keys.StrafeRight || keys.Right ? 1 : 0);
        var steeringTarget = steeringInput * _settings.MaxSteeringDegrees * MathF.PI / 180;
        var steeringRate = (steeringInput == 0 ? _settings.SteeringReturnDegrees : _settings.SteeringRateDegrees) * MathF.PI / 180;
        SteeringAngle = Approach(SteeringAngle, steeringTarget, steeringRate * seconds);

        var longitudinal = Vector2.Dot(Velocity, forward);
        var lateral = Vector2.Dot(Velocity, right);
        var braking = keys.Jump || (keys.Forward && keys.Backward) ||
                      (keys.Backward && longitudinal >= -_settings.StopSpeed &&
                       Velocity.LengthSquared() > _settings.StopSpeed * _settings.StopSpeed) ||
                      (keys.Forward && longitudinal < -_settings.StopSpeed);
        var throttle = braking ? 0 : keys.Forward ? 1 : keys.Backward ? -1 : 0;
        Throttle = throttle;
        Braking = braking ? 1 : 0;
        if (braking)
        {
            var speed = Velocity.Length();
            var scale = speed > 0 ? MathF.Max(0, speed - surface.BrakeDeceleration * seconds) / speed : 0;
            longitudinal *= scale;
            lateral *= scale;
        }
        else if (throttle != 0)
        {
            var maximum = throttle > 0 ? surface.MaxForwardSpeed : surface.MaxReverseSpeed;
            var acceleration = throttle > 0 ? surface.Acceleration : surface.ReverseAcceleration;
            var ratio = MathF.Max(0, longitudinal * throttle) / maximum;
            var taper = MathF.Max(0, 1 - ratio * ratio);
            // Keep launch torque, but stretch out the final part of forward acceleration.
            if (throttle > 0) taper /= 1 + 3 * ratio * ratio;
            var engine = acceleration * taper;
            longitudinal += throttle * engine * seconds;
        }

        var limit = longitudinal < 0 ? surface.MaxReverseSpeed : surface.MaxForwardSpeed;
        var totalSpeed = MathF.Sqrt(longitudinal * longitudinal + lateral * lateral);
        var excess = MathF.Max(0, totalSpeed - limit);
        if (excess > 0)
        {
            // The terrain penalty acts on motion, including sideways sliding.
            var scale = (limit + excess * MathF.Exp(-surface.SurfacePenaltyCoefficient * seconds)) / totalSpeed;
            longitudinal *= scale;
            lateral *= scale;
        }
        // The engine compensates rolling resistance under throttle, allowing
        // acceleration to approach the configured terminal speed.
        if (throttle == 0) longitudinal = Approach(longitudinal, 0, surface.RollingResistance * seconds);
        // Lateral grip is tire friction. Strong asphalt grip arrests skids faster;
        // lower off-road grip preserves the velocity vector longer through turns.
        lateral *= MathF.Exp(-surface.LateralGrip * seconds);
        Velocity = forward * longitudinal + right * lateral;
        if (throttle == 0 && Velocity.LengthSquared() < _settings.StopSpeed * _settings.StopSpeed)
            Velocity = Vector2.Zero;

        var yawTarget = longitudinal / _settings.Wheelbase * MathF.Tan(SteeringAngle);
        // Unequal rolling drag pulls the nose toward the muddy side when moving
        // forward, and reverses that yaw while backing up. Contact area does not
        // scale the penalty or pull: each side is either on mud or off it.
        yawTarget += pullSide * _settings.MudPullStrength * longitudinal / _settings.Asphalt.MaxForwardSpeed;
        var maximumCurvature = MathF.Tan(_settings.MaxSteeringDegrees * MathF.PI / 180) / _settings.Wheelbase;
        maximumCurvature += MathF.Abs(pullSide) * _settings.MudPullStrength / _settings.Asphalt.MaxForwardSpeed;
        var yawLimit = MathF.Min(
            MathF.Abs(longitudinal) * maximumCurvature,
            surface.MaxLateralAcceleration / MathF.Max(MathF.Abs(longitudinal), _settings.StopSpeed));
        yawTarget = Math.Clamp(yawTarget, -yawLimit, yawLimit);
        AngularVelocity += (yawTarget - AngularVelocity) * (1 - MathF.Exp(-_settings.AngularResponse * seconds));
        // Heading must follow distance travelled even while braking. Retaining
        // a previous yaw rate at crawling speed lets the body pivot in place.
        AngularVelocity = Math.Clamp(AngularVelocity, -yawLimit, yawLimit);
        if (Velocity == Vector2.Zero) AngularVelocity = 0;
        var rotation = AngularVelocity * seconds;
        var result = _collider.MoveAndSlide(transform.Position, forward, Velocity * seconds, rotation, Velocity);
        Velocity = result.Velocity;
        Acceleration = Vector2.Dot(Velocity - previousVelocity, forward) / seconds;
        if (result.Impact is { } impact) WorldImpact?.Invoke(impact);
        if (result.Collided)
        {
            // An impact can remove most of this step's planned motion. Do not
            // accumulate steering momentum while pushing into a barrier.
            var resolvedSpeed = MathF.Abs(Vector2.Dot(Velocity, result.Direction));
            var resolvedLimit = MathF.Min(resolvedSpeed * maximumCurvature,
                surface.MaxLateralAcceleration / MathF.Max(resolvedSpeed, _settings.StopSpeed));
            AngularVelocity = Math.Clamp(AngularVelocity, -resolvedLimit, resolvedLimit);
        }
        if (result.Collided && Vector2.DistanceSquared(result.Direction, Rotate(forward, rotation)) > .00000001f)
            AngularVelocity = 0;
        Parent.Transform = new Transform { Position = result.Position, Rotation = result.Direction };
    }

    private (CarSurfaceSettings Surface, int PullSide) SurfaceContact(Vector2 position, Vector2 forward, Vector2 right)
    {
        var extent = new Vector2(
            MathF.Abs(forward.X) * _settings.Length + MathF.Abs(right.X) * _settings.Width,
            MathF.Abs(forward.Y) * _settings.Length + MathF.Abs(right.Y) * _settings.Width) * .5f;
        var leftCenter = position - right * (_settings.Width * .25f);
        var rightCenter = position + right * (_settings.Width * .25f);
        var asphalt = false;
        var mud = false;
        var leftMud = false;
        var rightMud = false;
        // Test actual oriented footprint/cell intersections. Corner-only samples
        // miss thin patches touching an edge or the middle of the car.
        for (var x = (int)MathF.Floor(position.X - extent.X); x <= (int)MathF.Floor(position.X + extent.X); x++)
        for (var y = (int)MathF.Floor(position.Y - extent.Y); y <= (int)MathF.Floor(position.Y + extent.Y); y++)
        {
            if (!_collider.OverlapsCell(position, forward, x, y)) continue;
            if (_settings.RoadSurfaces.Contains(_surfaceAt(new(x + .5f, y + .5f)), StringComparer.Ordinal)) { asphalt = true; continue; }
            mud = true;
            leftMud |= _halfFootprint.OverlapsCell(leftCenter, forward, x, y);
            rightMud |= _halfFootprint.OverlapsCell(rightCenter, forward, x, y);
        }
        OnMud = mud;
        MudShakeStrength = !mud ? 0 : asphalt ? .5f : 1;
        var profile = !mud ? _settings.Asphalt : asphalt ? _mixedSurface : _settings.OffRoad;
        return (profile, (leftMud ? 1 : 0) - (rightMud ? 1 : 0));
    }

    private static float Approach(float value, float target, float change) =>
        value + Math.Clamp(target - value, -change, change);

    private static Vector2 Rotate(Vector2 direction, float angle)
    {
        var cosine = MathF.Cos(angle);
        var sine = MathF.Sin(angle);
        return new(direction.X * cosine - direction.Y * sine, direction.X * sine + direction.Y * cosine);
    }
}
