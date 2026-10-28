using System.Numerics;
using WolfNET.Engine.Actors;

namespace WolfNET.Engine.Physics;

public readonly record struct CarCollision(Vector2 Position, float Speed);

public static class CarContact
{
    private const float ContactTolerance = .002f;
    private const float RubbingResistance = 8;

    public static CarCollision? Resolve(CarObject first, CarObject second, NavigationMap map, float seconds,
        bool firstStopped, bool secondStopped)
    {
        if (!float.IsFinite(seconds) || seconds <= 0) return null;
        var a = first.Transform; var b = second.Transform;
        var normal = Vector2.Zero; var depth = float.PositiveInfinity;
        Span<Vector2> axes = stackalloc Vector2[] { a.Rotation, new(-a.Rotation.Y, a.Rotation.X),
            b.Rotation, new(-b.Rotation.Y, b.Rotation.X) };
        foreach (var axis in axes)
        {
            static float Radius(CarObject actor, Vector2 axis) => .5f *
                (actor.Settings.Length * MathF.Abs(Vector2.Dot(actor.Transform.Rotation, axis)) +
                 actor.Settings.Width * MathF.Abs(Vector2.Dot(new(-actor.Transform.Rotation.Y, actor.Transform.Rotation.X), axis)));
            var signed = Vector2.Dot(a.Position - b.Position, axis);
            var overlap = Radius(first, axis) + Radius(second, axis) - MathF.Abs(signed);
            // Keep rubbing across the tiny gap left by overlap correction.
            if (overlap < -ContactTolerance) return null;
            if (overlap < depth) { depth = overlap; normal = signed >= 0 ? axis : -axis; }
        }
        var massA = firstStopped ? 0f : 1f;
        var massB = secondStopped ? 0f : 1f;
        if (massA + massB == 0) return null;
        var boxA = new OrientedBoxCollider(map, first.Settings.Length, first.Settings.Width);
        var boxB = new OrientedBoxCollider(map, second.Settings.Length, second.Settings.Width);
        var correction = depth > 0 ? normal * (depth + .0001f) : Vector2.Zero;
        var moveA = correction * (massA / (massA + massB));
        var moveB = -correction * (massB / (massA + massB));
        if (!boxA.CanOccupy(a.Position + moveA, a.Rotation)) { moveA = Vector2.Zero; moveB = -correction; }
        if (!boxB.CanOccupy(b.Position + moveB, b.Rotation)) { moveB = Vector2.Zero; moveA = correction; }
        if (firstStopped || !boxA.CanOccupy(a.Position + moveA, a.Rotation)) moveA = Vector2.Zero;
        if (secondStopped || !boxB.CanOccupy(b.Position + moveB, b.Rotation)) moveB = Vector2.Zero;
        var velocityA = firstStopped ? Vector2.Zero : first.Controller.Velocity;
        var velocityB = secondStopped ? Vector2.Zero : second.Controller.Velocity;
        var closing = Vector2.Dot(velocityA - velocityB, normal);
        if (closing < 0)
        {
            var impulse = -closing / (massA + massB);
            velocityA += normal * (impulse * massA);
            velocityB -= normal * (impulse * massB);
        }
        // Body rubbing shares tangential momentum instead of allowing a free pass
        // along the other car. Separating cars can steer away without being held.
        if (closing <= 0)
        {
            var tangent = new Vector2(-normal.Y, normal.X);
            var sliding = Vector2.Dot(velocityA - velocityB, tangent);
            var impulse = -sliding * (1 - MathF.Exp(-RubbingResistance * seconds)) / (massA + massB);
            velocityA += tangent * (impulse * massA);
            velocityB -= tangent * (impulse * massB);
        }
        first.Controller.ResolveCarContact(a.Position + moveA, velocityA, resetAngularVelocity: firstStopped);
        second.Controller.ResolveCarContact(b.Position + moveB, velocityB, resetAngularVelocity: secondStopped);
        return closing < 0 ? new((a.Position + b.Position) * .5f, -closing) : null;
    }
}
