using System.Numerics;
using WolfSlop.Engine.World;
using WolfSlop.Engine.Physics;
using WolfSlop.Engine.Actors;

internal static class CarContactChecks
{
    private const float Step = 1f / 120;

    public static int Run()
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception(message);
        }

        var map = new NavigationMap();
        var cells = Enumerable.Range(0, 64).Select(x => Enumerable.Range(0, 128)
            .Select(y => x > 0 && x < 52 && y > 0 && y < 127).ToArray()).ToArray();
        map.Replace(cells, cells.Select(c => new bool[c.Length]).ToArray(),
            cells.Select(c => new bool[c.Length]).ToArray(),
            cells.Select(c => c.Select(_ => "asphalt").ToArray()).ToArray());
        var settings = new CarSettings();
        CarObject Create(Vector2 position, Vector2 velocity, CarSettings? profile = null,
            FakeKeyboard? keys = null, Vector2? heading = null)
        {
            var car = new CarObject(keys ?? new FakeKeyboard(), new FakeMouse(), map, profile ?? settings)
                { Transform = new() { Position = position, Rotation = heading ?? Vector2.UnitY } };
            car.Controller.ResolveCarContact(position, velocity);
            return car;
        }
        void Resolve(CarObject first, CarObject second, float seconds = Step, bool firstStopped = false, bool secondStopped = false)
            => CarContact.Resolve(first, second, map, seconds, firstStopped, secondStopped);
        void Advance(CarObject first, CarObject second, float seconds, float step = Step, bool reverse = false)
        {
            var frames = (int)MathF.Round(seconds / step);
            for (var frame = 0; frame < frames; frame++)
            {
                first.Update(step * 1000);
                second.Update(step * 1000);
                if (reverse) Resolve(second, first, step);
                else Resolve(first, second, step);
            }
        }

        // A car scraping along either rival must lose its passing advantage, even
        // once the initial overlap has been separated into a tiny clearance gap.
        foreach (var speedOffset in new[] { 3f, -3f })
        foreach (var side in new[] { -1, 1 })
        foreach (var reverse in new[] { false, true })
        {
            var rivalSettings = settings with
            {
                Asphalt = settings.Asphalt with { MaxForwardSpeed = settings.Asphalt.MaxForwardSpeed + speedOffset }
            };
            using var player = Create(new(48, 40), new(0, 20));
            using var blocker = Create(new(48 + side * (settings.Width - .005f), 40.7f), new(0, 16), rivalSettings);
            Advance(player, blocker, .5f, reverse: reverse);
            var speedAdvantage = player.Controller.Velocity.Y - blocker.Controller.Velocity.Y;
            Check(speedAdvantage >= 0 && speedAdvantage < .2f,
                "Body rubbing did not reduce the player's passing speed advantage.");
            Check(player.Transform.Position.Y < blocker.Transform.Position.Y,
                "The player freely squeezed ahead while rubbing against a blocking rival.");
            Check(MathF.Abs(player.Transform.Position.X - blocker.Transform.Position.X) >= settings.Width,
                "Rubbing cars remained interpenetrating.");
        }

        // Passing with clearance and steering apart must immediately avoid drag.
        using (var player = Create(new(48, 40), new(0, 20)))
        using (var blocker = Create(new(48 + settings.Width + .01f, 40.7f), new(0, 16)))
        {
            Advance(player, blocker, .5f);
            Check(player.Transform.Position.Y > blocker.Transform.Position.Y &&
                  player.Controller.Velocity.Y - blocker.Controller.Velocity.Y > 3.99f,
                "A clean pass was slowed despite having clearance.");
        }
        using (var player = Create(new(48, 40), new(-.1f, 20)))
        using (var blocker = Create(new(48 + settings.Width - .005f, 40), new(0, 16)))
        {
            Resolve(player, blocker, .25f);
            Check(player.Controller.Velocity == new Vector2(-.1f, 20) && blocker.Controller.Velocity == new Vector2(0, 16),
                "Separating cars were held back by rubbing resistance.");
        }

        // Momentum transfers symmetrically in the bodies' axes, with energy lost
        // through the impact and rubbing rather than a reversal of sliding.
        foreach (var heading in new[] { Vector2.UnitY, Vector2.Normalize(Vector2.One) })
        foreach (var reverse in new[] { false, true })
        {
            var right = new Vector2(heading.Y, -heading.X);
            using var first = Create(new(48, 40), heading * 20 + right, heading: heading);
            using var second = Create(first.Transform.Position + right * (settings.Width - .005f),
                heading * 16 - right, heading: heading);
            var momentum = first.Controller.Velocity + second.Controller.Velocity;
            var energy = first.Controller.Velocity.LengthSquared() + second.Controller.Velocity.LengthSquared();
            if (reverse) Resolve(second, first, .25f);
            else Resolve(first, second, .25f);
            var relative = first.Controller.Velocity - second.Controller.Velocity;
            Check(Vector2.Distance(first.Controller.Velocity + second.Controller.Velocity, momentum) < .0001f,
                "Moving car contact did not conserve combined momentum.");
            Check(first.Controller.Velocity.LengthSquared() + second.Controller.Velocity.LengthSquared() < energy,
                "Car contact added kinetic energy.");
            Check(Vector2.Dot(relative, heading) is > 0 and < 1 && MathF.Abs(Vector2.Dot(relative, right)) < .0001f,
                "Car contact reversed sliding or retained inward velocity.");
        }

        foreach (var reverse in new[] { false, true })
        {
            using var moving = Create(new(48, 40), new(0, 20));
            using var finished = Create(new(48 + settings.Width - .005f, 40), Vector2.Zero);
            var parkedPose = finished.Transform;
            if (reverse) Resolve(finished, moving, .25f, firstStopped: true);
            else Resolve(moving, finished, .25f, secondStopped: true);
            Check(finished.Transform.Position == parkedPose.Position && finished.Transform.Rotation == parkedPose.Rotation &&
                  finished.Controller.Velocity == Vector2.Zero &&
                  moving.Controller.Speed is > 0 and < 4,
                "Rubbing failed to slow a moving car or moved a finished car.");
        }

        // Contact must not cancel yaw every tick and prevent escape steering.
        var steeringKeys = new FakeKeyboard { KeyState = new() { Forward = true, StrafeLeft = true } };
        using (var steering = Create(new(48, 40), new(0, 20), keys: steeringKeys))
        {
            for (var frame = 0; frame < 12; frame++) steering.Update(Step * 1000);
            var angular = steering.Controller.AngularVelocity;
            var angle = steering.Controller.SteeringAngle;
            var heading = steering.Transform.Rotation;
            var right = new Vector2(heading.Y, -heading.X);
            using var blocker = Create(steering.Transform.Position + right * (settings.Width - .005f),
                heading * 16, heading: heading);
            Resolve(steering, blocker);
            Check(angular > .01f && steering.Controller.AngularVelocity == angular && steering.Controller.SteeringAngle == angle,
                "Body contact reset the steering car's angular motion.");
            Advance(steering, blocker, .5f);
            Check(Vector2.Dot(blocker.Transform.Position - steering.Transform.Position, right) > settings.Width + .05f,
                "A steering car could not pull away from rubbing contact.");
            steering.Controller.ResolveCarContact(steering.Transform.Position, Vector2.Zero);
            Check(steering.Controller.AngularVelocity == 0, "Explicitly stopping a car retained angular motion.");
        }

        Vector2[] ContactAtRate(float step)
        {
            using var player = Create(new(48, 40), new(0, 20));
            using var blocker = Create(new(48 + settings.Width - .005f, 40.7f), new(0, 16));
            Advance(player, blocker, .5f, step);
            return [player.Controller.Velocity, blocker.Controller.Velocity,
                player.Transform.Position, blocker.Transform.Position];
        }
        var reference = ContactAtRate(Step);
        foreach (var step in new[] { 1f / 30, 1f / 60 })
        {
            var candidate = ContactAtRate(step);
            Check(Vector2.Distance(reference[0], candidate[0]) < .001f &&
                  Vector2.Distance(reference[1], candidate[1]) < .001f &&
                  Vector2.Distance(reference[2], candidate[2]) < .06f &&
                  Vector2.Distance(reference[3], candidate[3]) < .06f,
                "Rubbing resistance changed strength with the simulation update rate.");
        }

        // Three cars rubbing beside a barrier must stay on the map and keep
        // moving; a finished car is covered separately above.
        var outerX = 52 - settings.Width * .5f - .0002f;
        using (var first = Create(new(outerX - 2 * (settings.Width - .005f), 40), new(1, 20)))
        using (var second = Create(new(outerX - (settings.Width - .005f), 40), new(1, 16)))
        using (var third = Create(new(outerX, 40), new(1, 18)))
        {
            CarObject[] cars = [first, second, third];
            for (var frame = 0; frame < 120; frame++)
            {
                foreach (var car in cars) car.Update(Step * 1000);
                for (var a = 0; a < cars.Length; a++)
                for (var b = a + 1; b < cars.Length; b++) Resolve(cars[a], cars[b]);
                Check(cars.All(car => new OrientedBoxCollider(map, settings.Length, settings.Width)
                          .CanOccupy(car.Transform.Position, car.Transform.Rotation) && car.Controller.Speed > 10),
                    "Three-car rubbing pushed a car through the barrier or stopped the pack.");
            }
        }

        using (var crashing = Create(new(50.5f, 40), new(20, 0), heading: Vector2.UnitX))
        {
            for (var frame = 0; frame < 6; frame++) crashing.Update(Step * 1000);
            Check(crashing.Controller.Speed < .1f && crashing.Look.Shake.Length() > .0001f,
                "A wall impact failed to shake the camera after stopping the car.");
        }

        Console.WriteLine($"PASS: {assertions} car contact assertions.");
        return assertions;
    }
}
