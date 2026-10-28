using System.Numerics;
using WolfNET.Engine.World;
using WolfNET.Engine.Core;
using WolfNET.Engine.Input;
using WolfNET.Engine.Physics;
using WolfNET.Engine.Actors;
using WolfNET.Engine.Actors.Components;

internal static class CarPhysicsChecks
{
    public static int Run()
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception(message);
        }

        var asphalt = Create();
        var fullMudContact = Create(surface: _ => "mud");
        var partialMudContact = Create(surface: position => position.Y >= 100 ? "mud" : "asphalt");
        asphalt.Controller.Update(1000f / 120);
        fullMudContact.Controller.Update(1000f / 120);
        partialMudContact.Controller.Update(1000f / 120);
        Check(!asphalt.Controller.OnMud, "Fully paved footprint must not report mud contact.");
        Check(fullMudContact.Controller.OnMud && partialMudContact.Controller.OnMud,
            "Partial and full mud contact must expose the same sound trigger.");
        Check(asphalt.Controller.MudShakeStrength == 0 && fullMudContact.Controller.MudShakeStrength == 1 &&
              partialMudContact.Controller.MudShakeStrength == .5f,
            "Mud shake must be absent on asphalt, full on mud, and exactly half for partial contact.");
        partialMudContact.Actor.Transform = new() { Position = new(100, 98), Rotation = Vector2.UnitX };
        partialMudContact.Controller.Update(1000f / 120);
        Check(!partialMudContact.Controller.OnMud, "Mud contact must clear when the entire footprint returns to asphalt.");
        Check(partialMudContact.Controller.MudShakeStrength == 0, "Mud shake must clear on returning to asphalt.");
        asphalt.Keys.KeyState = new() { Forward = true };
        Advance(asphalt.Controller, .5f);
        var earlyAcceleration = asphalt.Controller.Speed;
        Advance(asphalt.Controller, 4.5f);
        var laterSpeed = asphalt.Controller.Speed;
        Advance(asphalt.Controller, .5f);
        Check(earlyAcceleration > asphalt.Controller.Speed - laterSpeed && earlyAcceleration > 0,
            "Car acceleration must weaken as speed approaches its limit.");
        Advance(asphalt.Controller, 20);
        Check(asphalt.Controller.Speed <= new CarSettings().Asphalt.MaxForwardSpeed + .001f &&
              asphalt.Controller.Speed > new CarSettings().Asphalt.MaxForwardSpeed * .95f,
            "Asphalt forward speed did not approach its configured maximum.");

        float SpeedGain(float initialSpeed)
        {
            var rig = Create();
            rig.Controller.ResolveCarContact(rig.Actor.Transform.Position, new(initialSpeed, 0));
            rig.Keys.KeyState = new() { Forward = true };
            Advance(rig.Controller, .25f);
            return rig.Controller.Speed - initialSpeed;
        }
        var nearLimit = new CarSettings().Asphalt.MaxForwardSpeed * .9f;
        Check(SpeedGain(5) > 1.1f && SpeedGain(nearLimit) < .2f && SpeedGain(nearLimit) < SpeedGain(5) * .15f,
            "High-speed acceleration did not taper strongly while preserving low-speed pull.");

        var mud = Create(surface: _ => "mud");
        mud.Keys.KeyState = new() { Forward = true };
        Advance(mud.Controller, .5f);
        Check(mud.Controller.Speed < earlyAcceleration * .5f,
            "Mud acceleration must be substantially weaker than asphalt acceleration.");
        Advance(mud.Controller, 25);
        Check(mud.Controller.Speed <= 8.001f && mud.Controller.Speed > 7.2f &&
              mud.Controller.Speed < asphalt.Controller.Speed,
            "Mud forward limit must remain lower than the asphalt limit.");
        var grass = Create(surface: _ => "grass");
        grass.Keys.KeyState = new() { Forward = true };
        Advance(grass.Controller, 25.5f);
        Check(MathF.Abs(grass.Controller.Speed - mud.Controller.Speed) < .0001f,
            "Grass did not use the configured off-road profile.");

        // Fixed partial penalty: tiny and deep overlaps receive the same profile.
        var noPull = new CarSettings { MudPullStrength = 0 };
        string Edge(Vector2 point) => point.Y >= 101 ? "mud" : "asphalt";
        var edgeRoad = Create(noPull, Edge, position: new(100.5f, 100.5f));
        var shallow = Create(noPull, Edge, position: new(100.5f, 100.701f));
        var deep = Create(noPull, Edge, position: new(100.5f, 100.99f));
        var edgeMud = Create(noPull, Edge, position: new(100.5f, 101.5f));
        foreach (var rig in new[] { edgeRoad, shallow, deep, edgeMud })
        {
            rig.Keys.KeyState = new() { Forward = true };
            Advance(rig.Controller, .25f);
        }
        Check(MathF.Abs(shallow.Controller.Speed - deep.Controller.Speed) < .0001f,
            "Partial mud penalty changed with the amount of footprint overlap.");
        Check(shallow.Controller.Speed > edgeMud.Controller.Speed && shallow.Controller.Speed < edgeRoad.Controller.Speed,
            "A partly off-road car did not receive an intermediate acceleration penalty.");
        var firstPartial = Create(noPull, Edge, position: new(100.5f, 100.701f));
        firstPartial.Keys.KeyState = new() { Forward = true };
        firstPartial.Controller.Update(1000f / 120);
        Check(MathF.Abs(firstPartial.Controller.Speed * 120 - 5.2f) < .001f,
            "Mixed contact did not apply exactly half of the asphalt-to-mud acceleration penalty.");
        Advance(shallow.Controller, 45);
        Check(MathF.Abs(shallow.Controller.Speed - (noPull.Asphalt.MaxForwardSpeed + noPull.OffRoad.MaxForwardSpeed) * .5f) < .01f,
            "Partly off-road forward speed did not approach the halfway limit.");
        var footprintPatch = Create(noPull, point => (int)point.X == 100 && (int)point.Y == 100 ? "mud" : "asphalt",
            position: new(100.5f, 100.5f));
        footprintPatch.Actor.Transform = new() { Position = footprintPatch.Actor.Transform.Position, Rotation = Vector2.Normalize(Vector2.One) };
        footprintPatch.Keys.KeyState = new() { Forward = true };
        footprintPatch.Controller.Update(1000f / 120);
        Check(MathF.Abs(footprintPatch.Controller.Speed * 120 - 5.2f) < .001f,
            "Footprint sampling missed a mud patch between the car's corner samples.");
        var emptyCorner = Create(noPull, point => (int)point.X == 101 && (int)point.Y == 101 ? "mud" : "asphalt",
            position: new(100.4f, 100.4f));
        emptyCorner.Actor.Transform = new() { Position = emptyCorner.Actor.Transform.Position, Rotation = Vector2.Normalize(Vector2.One) };
        emptyCorner.Keys.KeyState = new() { Forward = true };
        emptyCorner.Controller.Update(1000f / 120);
        Check(MathF.Abs(emptyCorner.Controller.Speed * 120 - 8) < .001f,
            "Surface sampling penalized an empty corner of the car's axis-aligned bounding box.");
        var leftMud = Create(surface: Edge, position: new(100.5f, 100.8f));
        var rightMud = Create(surface: point => point.Y < 100 ? "mud" : "asphalt", position: new(100.5f, 100.2f));
        leftMud.Keys.KeyState = rightMud.Keys.KeyState = new() { Forward = true };
        Advance(leftMud.Controller, .5f); Advance(rightMud.Controller, .5f);
        Check(leftMud.Controller.AngularVelocity > .01f && leftMud.Actor.Transform.Rotation.Y > 0 &&
            rightMud.Controller.AngularVelocity < -.01f && rightMud.Actor.Transform.Rotation.Y < 0,
            "Unequal surface drag did not turn the car toward the muddy side.");
        var reverseEdge = Create(surface: Edge, position: new(100.5f, 100.8f));
        reverseEdge.Keys.KeyState = new() { Backward = true }; Advance(reverseEdge.Controller, .5f);
        Check(reverseEdge.Controller.AngularVelocity < 0, "Mud drag did not reverse yaw while backing up.");
        var parkedEdge = Create(surface: Edge, position: new(100.5f, 100.8f));
        Advance(parkedEdge.Controller, .5f);
        Check(parkedEdge.Actor.Transform.Rotation == Vector2.UnitX && parkedEdge.Controller.AngularVelocity == 0,
            "Mud contact rotated a stationary car.");
        var pullShallow = Create(surface: Edge, position: new(100.5f, 100.701f));
        var pullDeep = Create(surface: Edge, position: new(100.5f, 100.99f));
        pullShallow.Keys.KeyState = pullDeep.Keys.KeyState = new() { Forward = true };
        pullShallow.Controller.Update(1000f / 120); pullDeep.Controller.Update(1000f / 120);
        Check(MathF.Abs(pullShallow.Controller.AngularVelocity - pullDeep.Controller.AngularVelocity) < .0000001f,
            "Mud side pull changed with the amount of overlap on that side.");
        var mixedContact = false;
        var overspeedEdge = Create(noPull, point => mixedContact ? Edge(point) : "asphalt", position: new(100.5f, 100.8f));
        overspeedEdge.Keys.KeyState = new() { Forward = true }; Advance(overspeedEdge.Controller, 5);
        var beforePartial = overspeedEdge.Controller.Speed;
        mixedContact = true; overspeedEdge.Controller.Update(1000f / 120);
        var mixedLimit = (noPull.Asphalt.MaxForwardSpeed + noPull.OffRoad.MaxForwardSpeed) * .5f;
        var expectedPartial = mixedLimit + (beforePartial - mixedLimit) * MathF.Exp(-noPull.OffRoad.SurfacePenaltyCoefficient * .5f / 120);
        Check(overspeedEdge.Controller.Speed > mixedLimit && MathF.Abs(overspeedEdge.Controller.Speed - expectedPartial) < .001f,
            "Mixed contact clamped speed or failed to halve the progressive mud penalty coefficient.");

        var coast = Create();
        coast.Keys.KeyState = new() { Forward = true };
        Advance(coast.Controller, 2);
        var speedBeforeCoast = coast.Controller.Speed;
        var positionBeforeCoast = coast.Actor.Transform.Position;
        coast.Keys.KeyState = default;
        Advance(coast.Controller, 1);
        Check(coast.Controller.Speed > 0 && coast.Controller.Speed < speedBeforeCoast &&
              coast.Actor.Transform.Position.X > positionBeforeCoast.X,
            "Releasing throttle must preserve momentum while friction gradually slows the car.");
        var asphaltSpeedLoss = speedBeforeCoast - coast.Controller.Speed;
        var offRoadCoast = Create(surface: _ => "mud");
        offRoadCoast.Keys.KeyState = new() { Forward = true };
        Advance(offRoadCoast.Controller, 2);
        var offRoadSpeed = offRoadCoast.Controller.Speed;
        offRoadCoast.Keys.KeyState = default;
        Advance(offRoadCoast.Controller, 1);
        Check(asphaltSpeedLoss > offRoadSpeed - offRoadCoast.Controller.Speed,
            "Asphalt rolling friction must exceed off-road rolling friction.");

        string surfaceName = "asphalt";
        var transition = Create(surface: _ => surfaceName);
        transition.Keys.KeyState = new() { Forward = true };
        Advance(transition.Controller, 20);
        var beforeMud = transition.Controller.Speed;
        surfaceName = "mud";
        transition.Controller.Update(1000f / 120);
        Check(transition.Controller.Speed < beforeMud && transition.Controller.Speed > 8,
            "Entering mud must slow the car progressively instead of instantly clamping it.");
        Advance(transition.Controller, 3);
        Check(transition.Controller.Speed < 8.1f,
            "The off-road overspeed penalty did not bring speed down toward the lower limit.");
        float PenaltySpeed(float coefficient)
        {
            var name = "asphalt";
            var settings = new CarSettings();
            var rig = Create(settings with { OffRoad = settings.OffRoad with { SurfacePenaltyCoefficient = coefficient } },
                surface: _ => name);
            rig.Keys.KeyState = new() { Forward = true };
            Advance(rig.Controller, 20);
            name = "mud";
            Advance(rig.Controller, .25f);
            return rig.Controller.Speed;
        }
        Check(PenaltySpeed(5) < PenaltySpeed(.5f),
            "The configured surface penalty coefficient did not affect overspeed deceleration.");
        float SidewaysPenaltySpeed(float coefficient)
        {
            var name = "asphalt";
            var settings = new CarSettings();
            var rig = Create(settings with { OffRoad = settings.OffRoad with { SurfacePenaltyCoefficient = coefficient } },
                surface: _ => name);
            rig.Keys.KeyState = new() { Forward = true };
            Advance(rig.Controller, 20);
            rig.Actor.Transform = new() { Position = rig.Actor.Transform.Position, Rotation = Vector2.UnitY };
            rig.Keys.KeyState = default;
            name = "mud";
            Advance(rig.Controller, .25f);
            return rig.Controller.Speed;
        }
        Check(SidewaysPenaltySpeed(5) < SidewaysPenaltySpeed(.5f),
            "The off-road speed penalty ignored sideways car momentum.");

        foreach (var surface in new[] { "asphalt", "mud" })
        {
            var reverse = Create(surface: _ => surface);
            reverse.Keys.KeyState = new() { Backward = true };
            Advance(reverse.Controller, 20);
            var maximum = surface == "asphalt" ? 4 : 2;
            Check(reverse.Controller.SignedSpeed < -maximum * .85f && reverse.Controller.Speed <= maximum + .001f,
                $"Reverse acceleration or its slower {surface} limit is incorrect.");
            var reverseSpeed = reverse.Controller.Speed;
            reverse.Keys.KeyState = new() { Forward = true };
            Advance(reverse.Controller, .1f);
            Check(reverse.Controller.SignedSpeed < 0 && reverse.Controller.Speed < reverseSpeed,
                "Forward input must brake reverse travel before accelerating forward.");
            Advance(reverse.Controller, 5);
            Check(reverse.Controller.SignedSpeed > 0,
                "Forward input never accelerated after braking reverse travel.");
        }
        var brake = Create();
        brake.Keys.KeyState = new() { Forward = true };
        Advance(brake.Controller, 2);
        var brakingStart = brake.Controller.Speed;
        brake.Keys.KeyState = new() { Backward = true };
        Advance(brake.Controller, .1f);
        Check(brake.Controller.SignedSpeed > 0 && brake.Controller.Speed < brakingStart,
            "S must initially brake a moving forward car.");
        Advance(brake.Controller, 3);
        Check(brake.Controller.SignedSpeed < 0,
            "Holding S must start accelerated reverse after reaching a stop.");
        brake.Keys.KeyState = new() { Jump = true, Backward = true };
        Advance(brake.Controller, 3);
        Check(brake.Controller.Velocity == Vector2.Zero,
            "Space must take priority over reverse and hold the car stopped.");
        Check(!brake.Keys.ConsumeJump(), "The car left a jump action queued while using Space as a brake.");
        brake.Keys.KeyState = new() { Forward = true, Backward = true };
        Advance(brake.Controller, 1);
        Check(brake.Controller.Velocity == Vector2.Zero,
            "Opposing throttle inputs must brake rather than initiating reverse.");
        var slidingSettings = new CarSettings();
        var sidewaysBrake = Create(slidingSettings with
        {
            Asphalt = slidingSettings.Asphalt with { LateralGrip = .001f }
        });
        sidewaysBrake.Keys.KeyState = new() { Forward = true };
        Advance(sidewaysBrake.Controller, 2);
        var sidewaysSpeed = sidewaysBrake.Controller.Speed;
        sidewaysBrake.Actor.Transform = new() { Position = sidewaysBrake.Actor.Transform.Position, Rotation = Vector2.UnitY };
        sidewaysBrake.Keys.KeyState = new() { Backward = true };
        sidewaysBrake.Controller.Update(1000f / 120);
        Check(MathF.Abs(sidewaysBrake.Controller.SignedSpeed) < .0001f && sidewaysBrake.Controller.Speed < sidewaysSpeed,
            "S initiated reverse while the car was still sliding sideways.");

        var stationary = Create();
        var stationaryPose = stationary.Actor.Transform;
        stationary.Keys.KeyState = new() { StrafeLeft = true, Crouch = true };
        Advance(stationary.Controller, .5f);
        Check(stationary.Actor.Transform.Position == stationaryPose.Position &&
              stationary.Actor.Transform.Rotation == stationaryPose.Rotation && stationary.Controller.SteeringAngle > .17f,
            "A stationary car may turn its wheels but must neither strafe, crouch nor rotate its body.");
        stationary.Keys.KeyState = new() { Jump = true, Crouch = true };
        Advance(stationary.Controller, .5f);
        Check(stationary.Actor.Transform.Position == stationaryPose.Position && stationary.Controller.SteeringAngle == 0,
            "Jump/crouch input moved the car or released steering failed to center.");

        var turning = Create();
        turning.Keys.KeyState = new() { Forward = true };
        Advance(turning.Controller, 5);
        turning.Keys.KeyState = new() { Forward = true, StrafeLeft = true };
        turning.Controller.Update(1000f / 120);
        Check(turning.Controller.SteeringAngle > 0 && turning.Controller.SteeringAngle < .1f &&
              turning.Controller.AngularVelocity > 0,
            "Steering and angular velocity must build smoothly instead of jumping to their maximum.");
        Advance(turning.Controller, .25f);
        var heading = turning.Actor.Transform.Rotation;
        Check(heading.Y > 0 && MathF.Abs(Vector2.Dot(turning.Controller.Velocity, new(heading.Y, -heading.X))) > .01f,
            "Turning must affect body heading while preserving a physically separate velocity vector.");
        Check(turning.Controller.AngularVelocity <= 14 / MathF.Max(MathF.Abs(turning.Controller.SignedSpeed), .05f) + .1f,
            "High-speed turning exceeded the configured lateral-force limit.");
        turning.Keys.KeyState = new() { Forward = true };
        Advance(turning.Controller, .3f);
        Check(turning.Controller.SteeringAngle == 0, "Released steering did not automatically return to center.");
        var reverseTurn = Create();
        reverseTurn.Keys.KeyState = new() { Backward = true };
        Advance(reverseTurn.Controller, 3);
        reverseTurn.Keys.KeyState = new() { Backward = true, StrafeLeft = true };
        Advance(reverseTurn.Controller, .25f);
        Check(reverseTurn.Controller.AngularVelocity < 0 && reverseTurn.Actor.Transform.Rotation.Y < 0,
            "Reverse steering must rotate the car in the opposite direction.");
        Advance(reverseTurn.Controller, 4);
        var reverseRadius = reverseTurn.Controller.Speed / MathF.Abs(reverseTurn.Controller.AngularVelocity);
        var dimensions = new CarSettings();
        var minimumRadius = dimensions.Wheelbase / MathF.Tan(dimensions.MaxSteeringDegrees * MathF.PI / 180);
        Check(reverseRadius > minimumRadius * .95f && reverseRadius < minimumRadius * 1.1f,
            $"Full-lock low-speed steering did not match the wheelbase: radius {reverseRadius}");

        var brakingTurn = Create();
        brakingTurn.Keys.KeyState = new() { Forward = true, StrafeLeft = true };
        Advance(brakingTurn.Controller, 2);
        brakingTurn.Keys.KeyState = new() { Jump = true, StrafeLeft = true };
        var traveledWhileBraking = 0f;
        var rotatedWhileBraking = 0f;
        for (var step = 0; step < 240; step++)
        {
            var previous = brakingTurn.Actor.Transform;
            brakingTurn.Controller.Update(1000f / 120);
            var current = brakingTurn.Actor.Transform;
            traveledWhileBraking += Vector2.Distance(previous.Position, current.Position);
            rotatedWhileBraking += MathF.Abs(MathF.Atan2(
                previous.Rotation.X * current.Rotation.Y - previous.Rotation.Y * current.Rotation.X,
                Vector2.Dot(previous.Rotation, current.Rotation)));
            Check(MathF.Abs(brakingTurn.Controller.AngularVelocity) <= brakingTurn.Controller.Speed / 3.5f + .001f,
                "Braking retained a yaw rate incompatible with the car's remaining travel speed.");
        }
        Check(brakingTurn.Controller.Speed == 0 && brakingTurn.Controller.AngularVelocity == 0 &&
            rotatedWhileBraking <= traveledWhileBraking / 3.5f + .001f,
            "A braking turn pivoted the body without travelling the required turning arc.");
        var arrows = Create();
        arrows.Keys.KeyState = new() { Forward = true, Right = true };
        Advance(arrows.Controller, 1);
        Check(arrows.Controller.SteeringAngle < 0 && arrows.Actor.Transform.Rotation.Y < 0,
            "Arrow steering did not use the same car physics as A/D.");

        float LateralSlip(float grip)
        {
            var settings = new CarSettings();
            var rig = Create(settings with { Asphalt = settings.Asphalt with { LateralGrip = grip } });
            rig.Keys.KeyState = new() { Forward = true };
            Advance(rig.Controller, 5);
            rig.Keys.KeyState = new() { Forward = true, StrafeLeft = true };
            Advance(rig.Controller, .6f);
            var direction = rig.Actor.Transform.Rotation;
            return MathF.Abs(Vector2.Dot(rig.Controller.Velocity, new(direction.Y, -direction.X)));
        }
        Check(LateralSlip(1) > LateralSlip(15), "Higher tire friction did not reduce lateral slipping.");

        foreach (var frame in new[] { 1000f / 30, 1000f / 60, 1000f / 144 })
        {
            var reference = Create();
            reference.Keys.KeyState = new() { Forward = true, StrafeRight = true };
            Advance(reference.Controller, 3, 1000f / 120);
            var candidate = Create();
            candidate.Keys.KeyState = reference.Keys.KeyState;
            Advance(candidate.Controller, 3, frame);
            Check(Vector2.Distance(reference.Actor.Transform.Position, candidate.Actor.Transform.Position) < .0001f &&
                  Vector2.Distance(reference.Controller.Velocity, candidate.Controller.Velocity) < .0001f &&
                  Vector2.Distance(reference.Actor.Transform.Rotation, candidate.Actor.Transform.Rotation) < .0001f,
                $"Car physics changed with render frame duration {frame}ms.");
        }
        var paused = Create();
        paused.Keys.KeyState = new() { Forward = true, StrafeLeft = true };
        Advance(paused.Controller, 1);
        var savedVelocity = paused.Controller.Velocity;
        var savedSteering = paused.Controller.SteeringAngle;
        var savedAngular = paused.Controller.AngularVelocity;
        paused.Controller.ResetTiming();
        Check(paused.Controller.Velocity == savedVelocity && paused.Controller.SteeringAngle == savedSteering &&
              paused.Controller.AngularVelocity == savedAngular,
            "Pausing/resetting time discarded car momentum or steering state.");

        var wallNavigation = new TestNavigation((x, _) => x < 10);
        var sportsCar = new OrientedBoxCollider(wallNavigation, dimensions.Length, dimensions.Width);
        Check(sportsCar.CanOccupy(new(9.249f, 5.5f), Vector2.UnitX) &&
              !sportsCar.CanOccupy(new(9.251f, 5.5f), Vector2.UnitX),
            "Sports-car collision did not use the 4.5-meter length at 3 meters per block.");
        Check(sportsCar.CanOccupy(new(9.682f, 5.5f), Vector2.UnitY) &&
              !sportsCar.CanOccupy(new(9.685f, 5.5f), Vector2.UnitY),
            "Sports-car collision did not use the 1.9-meter width at 3 meters per block.");
        var collider = new OrientedBoxCollider(wallNavigation, .9f, .6f);
        Check(collider.CanOccupy(new(9.54f, 5.5f), Vector2.UnitX) &&
              !collider.CanOccupy(new(9.56f, 5.5f), Vector2.UnitX),
            "Car collision did not use its requested 0.9-block length.");
        Check(collider.CanOccupy(new(9.69f, 5.5f), Vector2.UnitY) &&
              !collider.CanOccupy(new(9.71f, 5.5f), Vector2.UnitY),
            "Car collision did not use its requested 0.6-block width.");
        var collision = collider.MoveAndSlide(new(5.5f, 5.5f), Vector2.UnitX, new(40, 0), 0, new(40, 0));
        Check(collision.Collided && MathF.Abs(collision.Position.X - 9.55f) < .0001f && collision.Velocity.X == 0 &&
              collider.CanOccupy(collision.Position, collision.Direction),
            "Fast car movement tunneled through a wall or retained inward collision velocity.");
        var diagonal = Vector2.Normalize(new Vector2(1, .3f));
        var slide = collider.MoveAndSlide(new(5.5f, 5.5f), diagonal, new(8, 3), 0, new(8, 3));
        Check(slide.Collided && slide.Position.Y > 8.4f && slide.Velocity.Y > 0 && MathF.Abs(slide.Velocity.X) < .0001f &&
              collider.CanOccupy(slide.Position, slide.Direction),
            "Car contact did not preserve tangential sliding along a wall.");
        var corridor = new OrientedBoxCollider(new TestNavigation((x, _) => x == 5), .9f, .6f);
        var throughCorridor = corridor.MoveAndSlide(new(5.5f, 2), Vector2.UnitY, new(0, 20), 0, new(0, 20));
        Check(Vector2.Distance(throughCorridor.Position, new(5.5f, 22)) < .001f,
            "The oriented car could not fit through a one-block-wide straight lane.");
        var rotation = corridor.MoveAndSlide(new(5.5f, 5.5f), Vector2.UnitY, Vector2.Zero, MathF.PI / 2, Vector2.Zero);
        Check(rotation.Collided && corridor.CanOccupy(rotation.Position, rotation.Direction) &&
              MathF.Abs(rotation.Direction.Y) > .5f,
            "Rotation swept the car through a wall in a tight corridor.");
        var cornerCollider = new OrientedBoxCollider(new TestNavigation((x, y) => x != 10 || y != 10), .9f, .6f);
        var diagonalHeading = Vector2.Normalize(Vector2.One);
        Check(cornerCollider.CanOccupy(new(9.5f, 9.5f), diagonalHeading) &&
              !cornerCollider.CanOccupy(new(9.7f, 9.7f), diagonalHeading),
            "Oriented rectangle corner contact used an axis-aligned or circular approximation.");
        var wallCar = Create(navigation: wallNavigation, position: new(5.5f, 5.5f));
        wallCar.Keys.KeyState = new() { Forward = true };
        Advance(wallCar.Controller, 10);
        Check(MathF.Abs(wallCar.Actor.Transform.Position.X - (10 - dimensions.Length * .5f)) < .0001f && wallCar.Controller.Speed < .001f,
            "Repeated throttle against a barrier let the car overlap scenery or accumulate hidden speed.");
        wallCar.Keys.KeyState = new() { Forward = true, StrafeLeft = true };
        Advance(wallCar.Controller, 2);
        Check(MathF.Abs(MathF.Atan2(wallCar.Actor.Transform.Rotation.Y, wallCar.Actor.Transform.Rotation.X)) < .01f &&
            MathF.Abs(wallCar.Controller.AngularVelocity) < .002f,
            "Steering accumulated yaw while forward motion was blocked by a barrier.");

        return assertions;
    }

    private static (TestObject Actor, FakeKeyboard Keys, CarController Controller) Create(CarSettings? settings = null,
        Func<Vector2, string>? surface = null, INavigationMap? navigation = null, Vector2? position = null)
    {
        var actor = new TestObject { Transform = new() { Position = position ?? new Vector2(100, 100), Rotation = Vector2.UnitX } };
        var keys = new FakeKeyboard();
        var controller = new CarController(actor, keys, navigation ?? new TestNavigation((_, _) => true),
            surface ?? (_ => "asphalt"), settings ?? new());
        return (actor, keys, controller);
    }

    private static void Advance(CarController controller, float seconds, float frameMilliseconds = 1000f / 120)
    {
        var remaining = (double)seconds * 1000;
        while (remaining > .00001)
        {
            var step = (float)Math.Min(remaining, frameMilliseconds);
            controller.Update(step);
            remaining -= step;
        }
    }

    private sealed class TestNavigation(Func<int, int, bool> walkable) : INavigationMap
    {
        public bool IsWalkable(int x, int y, bool inAir = false, bool crouching = false) => walkable(x, y);
    }
}
