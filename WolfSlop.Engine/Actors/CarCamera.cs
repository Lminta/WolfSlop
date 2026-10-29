using System.Numerics;
using WolfSlop.Engine.Physics;
using WolfSlop.Engine.Graphics;

namespace WolfSlop.Engine.Actors;

/// <summary>Free look around the car's heading, with a collision-aware chase camera.</summary>
public sealed class CarCamera
{
    private const float CollisionRadius = .1f;
    private const float SweepStep = .025f;
    private readonly CameraShakeSettings _shakeSettings;
    private float _idleSeconds;
    private double _shakeSeconds;
    private double _mudContactSeconds;
    private float _shakeAmplitude;
    private Vector2 _mudShake;
    private float _impactAmplitude;
    private float _impactSeconds;
    private float _impactDuration;
    private float _impactFrequency;

    public float Yaw { get; private set; }
    public float Pitch { get; private set; }
    // Visual yaw/pitch offsets in radians; never applied to the car's transform.
    public Vector2 Shake => _mudShake + ImpactShake;

    public CarCamera(CameraShakeSettings? shakeSettings = null)
    {
        _shakeSettings = shakeSettings ?? new();
        _shakeSettings.Validate();
    }

    private Vector2 ImpactShake
    {
        get
        {
            if (_impactSeconds >= _impactDuration) return Vector2.Zero;
            var remaining = 1 - _impactSeconds / _impactDuration;
            var phase = MathF.Tau * _impactFrequency * _impactSeconds;
            return _impactAmplitude * remaining * remaining *
                new Vector2(_shakeSettings.ImpactYawScale * MathF.Cos(phase * _shakeSettings.ImpactYawFrequencyScale), MathF.Cos(phase));
        }
    }

    public void TriggerImpact(float speed)
    {
        if (!float.IsFinite(speed) || speed < 0)
            throw new ArgumentOutOfRangeException(nameof(speed));
        // Ignore gentle rubbing; use collision closing speed, not driving speed.
        if (speed <= _shakeSettings.ImpactMinSpeed) return;
        var severity = Math.Clamp((speed - _shakeSettings.ImpactMinSpeed) /
            (_shakeSettings.ImpactFullStrengthSpeed - _shakeSettings.ImpactMinSpeed), 0, 1);
        var amplitude = _shakeSettings.ImpactAmplitude * severity;
        var remaining = _impactDuration > 0 ? MathF.Max(0, 1 - _impactSeconds / _impactDuration) : 0;
        if (amplitude <= _impactAmplitude * remaining * remaining) return;
        _impactAmplitude = amplitude;
        _impactSeconds = 0;
        _impactDuration = _shakeSettings.ImpactMinDuration +
            (_shakeSettings.ImpactMaxDuration - _shakeSettings.ImpactMinDuration) * severity;
        _impactFrequency = _shakeSettings.ImpactLowSpeedFrequency +
            (_shakeSettings.ImpactHighSpeedFrequency - _shakeSettings.ImpactLowSpeedFrequency) * severity;
    }

    public void UpdateShake(float deltaSeconds, float speed, float mudStrength)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0 ||
            !float.IsFinite(speed) || speed < 0 || !float.IsFinite(mudStrength) || mudStrength < 0 || mudStrength > 1)
            throw new ArgumentOutOfRangeException();

        var seconds = Math.Min(deltaSeconds, .25f);
        _impactSeconds += seconds;
        _shakeSeconds += seconds;
        // Brief touches must not accumulate into a shake when skimming the road edge.
        var previousContact = _mudContactSeconds;
        _mudContactSeconds = mudStrength > 0 ? _mudContactSeconds + seconds : 0;
        var activeSeconds = mudStrength > 0
            ? (float)Math.Max(0, _mudContactSeconds - Math.Max(previousContact, _shakeSettings.MudEntryDelay)) : seconds;
        var targetAmplitude = _mudContactSeconds > _shakeSettings.MudEntryDelay
            ? _shakeSettings.MudAmplitude * mudStrength * Math.Clamp(speed / _shakeSettings.MudFullStrengthSpeed, 0, 1) : 0;
        // Ease in and out, including changes between partial and full contact.
        var smoothingSeconds = targetAmplitude > 0 ? activeSeconds : seconds;
        var response = targetAmplitude > _shakeAmplitude ? _shakeSettings.MudFadeInRate : _shakeSettings.MudFadeOutRate;
        _shakeAmplitude += (targetAmplitude - _shakeAmplitude) * (1 - MathF.Exp(-response * smoothingSeconds));
        if (speed == 0 || _shakeAmplitude < .000001f) _shakeAmplitude = 0;
        float Wave(float frequency) => (float)Math.Sin(Math.Tau * frequency * _shakeSeconds);
        _mudShake = _shakeAmplitude * new Vector2(
            .6f * Wave(_shakeSettings.MudYawFrequency) + .4f * Wave(_shakeSettings.MudYawDetailFrequency),
            .65f * Wave(_shakeSettings.MudPitchFrequency) + .35f * Wave(_shakeSettings.MudPitchDetailFrequency));
    }

    public void Update(float deltaSeconds, Vector2 mousePixels, CarSettings settings)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (!float.IsFinite(mousePixels.X) || !float.IsFinite(mousePixels.Y))
            throw new ArgumentOutOfRangeException(nameof(mousePixels));

        if (mousePixels != Vector2.Zero)
        {
            // Wrap instead of clamping so the chase camera can orbit repeatedly.
            Yaw = MathF.IEEERemainder(Yaw - mousePixels.X * settings.MouseSensitivity, MathF.Tau);
            Pitch = Math.Clamp(Pitch - mousePixels.Y * settings.MouseSensitivity,
                -Radians(settings.CameraMaxPitchDegrees), Radians(settings.CameraMaxPitchDegrees));
            _idleSeconds = 0;
            return;
        }

        var previousIdle = _idleSeconds;
        _idleSeconds += deltaSeconds;
        // Only the part of this update after the delay contributes to centering.
        var recenterSeconds = MathF.Max(0, _idleSeconds - MathF.Max(previousIdle, settings.CameraRecenterDelay));
        var factor = MathF.Exp(-settings.CameraRecenterRate * recenterSeconds);
        Yaw *= factor;
        Pitch *= factor;
        if (MathF.Abs(Yaw) < .00001f) Yaw = 0;
        if (MathF.Abs(Pitch) < .00001f) Pitch = 0;
    }

    public Vector2 Direction(Transform body)
    {
        var heading = Vector2.Normalize(body.Rotation);
        var cosine = MathF.Cos(Yaw);
        var sine = MathF.Sin(Yaw);
        return new(heading.X * cosine - heading.Y * sine, heading.X * sine + heading.Y * cosine);
    }

    public Vector2 Plane(Transform body, float planeScale)
    {
        var direction = Direction(body);
        return new Vector2(direction.Y, -direction.X) * planeScale;
    }

    public CameraView View(Transform body, bool thirdPerson, RenderMap map, CarSettings settings)
    {
        if (!thirdPerson) return new(body.Position, 0, 0);

        var desiredDistance = settings.CameraFollowDistance * MathF.Cos(Pitch);
        var boom = -Direction(body) * desiredDistance;
        var fraction = 0f;
        if (desiredDistance > 0)
        {
            for (var next = SweepStep; next <= desiredDistance + SweepStep; next += SweepStep)
            {
                var step = MathF.Min(next / desiredDistance, 1);
                if (!Clear(body.Position + boom * step, map)) break;
                fraction = step;
                if (step == 1) break;
            }
        }
        var distance = desiredDistance * fraction;
        var elevation = Math.Clamp(settings.CameraElevation - MathF.Tan(Pitch) * distance, -.45f, .45f);
        return new(body.Position + boom * fraction, elevation, distance);
    }

    public void Reset()
    {
        ResetOrbit();
        _shakeSeconds = 0;
        _mudContactSeconds = 0;
        _shakeAmplitude = 0;
        _mudShake = Vector2.Zero;
        _impactAmplitude = _impactSeconds = _impactDuration = _impactFrequency = 0;
    }

    internal void ResetOrbit()
    {
        Yaw = Pitch = _idleSeconds = 0;
    }

    private static float Radians(float degrees) => degrees * (MathF.PI / 180);

    private static bool Clear(Vector2 position, RenderMap map)
    {
        for (var x = (int)MathF.Floor(position.X - CollisionRadius); x <= (int)MathF.Floor(position.X + CollisionRadius); x++)
        for (var y = (int)MathF.Floor(position.Y - CollisionRadius); y <= (int)MathF.Floor(position.Y + CollisionRadius); y++)
        {
            if (x < 0 || y < 0 || x >= map.Width || y >= map.Height) return false;
            var cell = map.Cells[y * map.Width + x];
            if ((cell.Flags & (RenderCellFlags.Wall | RenderCellFlags.StopsRay)) == 0) continue;
            if (CircleGeometry.OverlapsCell(position, CollisionRadius, x, y)) return false;
        }
        return true;
    }
}
