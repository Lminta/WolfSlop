using System.Numerics;
using WolfSlop.Engine.World;
using WolfSlop.Engine.Actors;
using WolfSlop.Engine.Graphics;

internal static class CarCameraChecks
{
    public static int Run()
    {
        var count = 0;
        void Check(bool condition, string message)
        {
            count++;
            if (!condition) throw new Exception(message);
        }
        void Near(float actual, float expected, string message) =>
            Check(MathF.Abs(actual - expected) < .0001f, message);
        void PositionNear(Vector2 actual, Vector2 expected, string message) =>
            Check(Vector2.DistanceSquared(actual, expected) < .000001f, message);

        var settings = new CarSettings();
        var body = new Transform { Position = new(7.5f, 7.5f), Rotation = Vector2.UnitX };
        var camera = new CarCamera();
        var map = Map();
        var view = camera.View(body, true, map, settings);
        Near(view.Distance, settings.CameraFollowDistance, "Car chase camera lost configured follow distance.");
        Near(view.Elevation, settings.CameraElevation, "Car chase camera lost configured elevation.");
        PositionNear(view.Position, body.Position - Vector2.UnitX * settings.CameraFollowDistance,
            "Car chase camera is not behind the body heading.");
        var cockpit = camera.View(body, false, map, settings);
        PositionNear(cockpit.Position, body.Position, "Cockpit camera moved off the car.");
        Check(cockpit.Distance == 0 && cockpit.Elevation == 0, "Cockpit camera retained third-person offsets.");

        camera.Update(.01f, new(100, -50), settings);
        Near(camera.Yaw, -100 * settings.MouseSensitivity, "Car free look has the wrong horizontal sensitivity.");
        Near(camera.Pitch, 50 * settings.MouseSensitivity, "Car free look has the wrong vertical sensitivity.");
        PositionNear(body.Rotation, Vector2.UnitX, "Free look changed the car body heading.");
        var direction = camera.Direction(body);
        var plane = camera.Plane(body, .8f);
        Near(direction.Length(), 1, "Car view direction is not normalized.");
        Near(plane.Length(), .8f, "Car free look changed the field of view.");
        Near(Vector2.Dot(direction, plane), 0, "Car camera plane is not perpendicular to its view direction.");
        var orbit = camera.View(body, true, map, settings);
        PositionNear(orbit.Position, body.Position - direction * orbit.Distance,
            "Third-person free look did not orbit around the car.");

        var heldYaw = camera.Yaw;
        var heldPitch = camera.Pitch;
        camera.Update(settings.CameraRecenterDelay, Vector2.Zero, settings);
        Near(camera.Yaw, heldYaw, "Car free look recentered before its delay elapsed.");
        Near(camera.Pitch, heldPitch, "Car pitch recentered before its delay elapsed.");
        camera.Update(.25f, Vector2.Zero, settings);
        Near(camera.Yaw, heldYaw * MathF.Exp(-settings.CameraRecenterRate * .25f),
            "Car free look did not recenter exponentially after the delay.");
        camera.Update(.01f, new(1, 0), settings);
        heldYaw = camera.Yaw;
        camera.Update(settings.CameraRecenterDelay, Vector2.Zero, settings);
        Near(camera.Yaw, heldYaw, "New mouse movement did not restart the recenter delay.");

        var oneFrame = new CarCamera();
        var manyFrames = new CarCamera();
        oneFrame.Update(0, new(200, 100), settings);
        manyFrames.Update(0, new(200, 100), settings);
        oneFrame.Update(2.25f, Vector2.Zero, settings);
        for (var i = 0; i < 225; i++) manyFrames.Update(.01f, Vector2.Zero, settings);
        Near(oneFrame.Yaw, manyFrames.Yaw, "Car recentering depends on the frame rate.");
        Near(oneFrame.Pitch, manyFrames.Pitch, "Car pitch recentering depends on the frame rate.");

        camera.Reset();
        camera.Update(0, new(-MathF.PI / settings.MouseSensitivity, -100000), settings);
        Near(MathF.Abs(camera.Yaw), MathF.PI, "Car orbit could not reach the view ahead of the car looking backward.");
        PositionNear(camera.Direction(body), -body.Rotation, "Rearward orbit did not reverse the viewing direction.");
        Near(camera.Pitch, settings.CameraMaxPitchDegrees * MathF.PI / 180, "Positive car free-look pitch exceeded its limit.");
        camera.Update(0, new(-MathF.PI / settings.MouseSensitivity, 100000), settings);
        Near(camera.Yaw, 0, "A complete orbit did not wrap smoothly back behind the car.");
        Near(camera.Pitch, -settings.CameraMaxPitchDegrees * MathF.PI / 180, "Negative car free-look pitch exceeded its limit.");
        camera.Update(0, new(MathF.Tau * 3.25f / settings.MouseSensitivity, 0), settings);
        Near(camera.Yaw, -MathF.PI / 2, "Repeated reverse orbits were clamped instead of wrapped.");
        camera.Reset();
        Check(camera.Yaw == 0 && camera.Pitch == 0, "Reset left car free-look offsets behind.");

        foreach (var flags in new[] { RenderCellFlags.Wall, RenderCellFlags.StopsRay })
        {
            var blocked = Map((6, 7, flags));
            var stopped = camera.View(body, true, blocked, settings);
            Check(stopped.Distance is >= .3f and <= .41f,
                "Car chase camera passed through a wall or ray-blocking barrier.");
            Check(stopped.Position.X >= 7.099f, "Car chase camera lost its wall clearance.");
        }
        var edge = new Transform { Position = new(.5f, 7.5f), Rotation = Vector2.UnitX };
        var edgeView = camera.View(edge, true, map, settings);
        Check(edgeView.Distance == 0, "Car chase camera left the map boundary.");

        var fullMud = new CarCamera();
        var partialMud = new CarCamera();
        var maximumShake = 0f;
        for (var i = 0; i < 120; i++)
        {
            fullMud.UpdateShake(1f / 120, 8, 1);
            partialMud.UpdateShake(1f / 120, 8, .5f);
            maximumShake = MathF.Max(maximumShake, fullMud.Shake.Length());
            PositionNear(partialMud.Shake, fullMud.Shake * .5f,
                "Partial mud did not produce exactly half the shake amplitude.");
            Check(fullMud.Shake.Length() <= .004f * MathF.Sqrt(2), "Mud shake exceeded its reduced amplitude limit.");
        }
        Check(maximumShake > .0001f, "Moving on mud produced no camera shake.");
        Check(fullMud.Yaw == 0 && fullMud.Pitch == 0, "Mud shake altered the saved free-look angles.");
        var steadyView = fullMud.View(body, true, map, settings);
        PositionNear(steadyView.Position, camera.View(body, true, map, settings).Position,
            "Mud shake moved the collision-aware camera position.");
        fullMud.UpdateShake(.02f, 8, 0);
        Check(fullMud.Shake.Length() > 0 && fullMud.Shake.Length() < .004f * MathF.Sqrt(2) * .7f,
            "Mud shake must fade out smoothly after leaving mud.");
        fullMud.UpdateShake(.25f, 8, 0);
        fullMud.UpdateShake(.25f, 8, 0);
        Check(fullMud.Shake == Vector2.Zero, "Mud shake did not finish fading out on asphalt.");
        fullMud.UpdateShake(.01f, 0, 1);
        Check(fullMud.Shake == Vector2.Zero, "Stationary cars must not shake on mud.");
        fullMud.UpdateShake(.01f, 8, 0);
        Check(fullMud.Shake == Vector2.Zero, "Camera shake persisted on asphalt.");
        fullMud.UpdateShake(.01f, 8, 1);
        fullMud.Reset();
        Check(fullMud.Shake == Vector2.Zero, "Reset left mud shake behind.");

        var briefContact = new CarCamera();
        for (var i = 0; i < 5; i++)
        {
            briefContact.UpdateShake(.09f, 8, 1);
            Check(briefContact.Shake == Vector2.Zero, "A brief mud touch triggered camera shake.");
            briefContact.UpdateShake(.01f, 8, 0);
        }
        briefContact.UpdateShake(.09f, 8, .5f);
        Check(briefContact.Shake == Vector2.Zero, "Separate mud touches accumulated past the entry delay.");
        briefContact.UpdateShake(.02f, 8, .5f);
        Check(briefContact.Shake.Length() > 0 && briefContact.Shake.Length() < .0003f,
            "Sustained mud contact did not ease into shake after the entry delay.");

        var gentleImpact = new CarCamera();
        var hardImpact = new CarCamera();
        gentleImpact.TriggerImpact(.5f);
        Check(gentleImpact.Shake == Vector2.Zero, "Gentle rubbing triggered impact shake.");
        gentleImpact.TriggerImpact(3);
        hardImpact.TriggerImpact(20);
        Check(gentleImpact.Shake.Length() > 0 && hardImpact.Shake.Length() > gentleImpact.Shake.Length() * 5,
            "Impact shake did not grow with collision speed.");
        var hardKick = hardImpact.Shake;
        hardImpact.TriggerImpact(1);
        PositionNear(hardImpact.Shake, hardKick, "A minor contact replaced a stronger impact kick.");
        gentleImpact.UpdateShake(.2f, 0, 0);
        hardImpact.UpdateShake(.2f, 0, 0);
        Check(gentleImpact.Shake == Vector2.Zero && hardImpact.Shake.Length() > .0001f,
            "Harder impacts must last longer and remain visible after the car stops.");
        hardImpact.UpdateShake(.25f, 0, 0);
        hardImpact.UpdateShake(.11f, 0, 0);
        Check(hardImpact.Shake == Vector2.Zero, "Impact shake did not settle back to zero.");
        hardImpact.TriggerImpact(100);
        Near(hardImpact.Shake.Y, .05f, "Extreme impacts exceeded the maximum kick amplitude.");
        hardImpact.Reset();
        Check(hardImpact.Shake == Vector2.Zero, "Reset retained impact shake.");

        var singleImpactFrame = new CarCamera();
        var multipleImpactFrames = new CarCamera();
        singleImpactFrame.TriggerImpact(12);
        multipleImpactFrames.TriggerImpact(12);
        singleImpactFrame.UpdateShake(.1f, 0, 0);
        for (var i = 0; i < 10; i++) multipleImpactFrames.UpdateShake(.01f, 0, 0);
        PositionNear(singleImpactFrame.Shake, multipleImpactFrames.Shake,
            "Impact shake changed with the update rate.");

        var customSettings = new CameraShakeSettings
        {
            MudAmplitude = .008f, MudEntryDelay = .2f,
            ImpactAmplitude = .1f, ImpactMinSpeed = 2, ImpactFullStrengthSpeed = 10,
            ImpactMinDuration = .1f, ImpactMaxDuration = .2f, ImpactYawScale = .25f
        };
        var custom = new CarCamera(customSettings);
        custom.UpdateShake(.15f, 8, 1);
        Check(custom.Shake == Vector2.Zero, "Custom mud entry delay was ignored.");
        custom.UpdateShake(.1f, 8, 1);
        Check(custom.Shake.Length() > 0, "Custom mud shake never activated.");
        custom.Reset();
        custom.TriggerImpact(1);
        Check(custom.Shake == Vector2.Zero, "Custom impact speed threshold was ignored.");
        custom.TriggerImpact(10);
        Near(custom.Shake.Y, .1f, "Custom impact amplitude or full-strength speed was ignored.");
        Near(custom.Shake.X, .025f, "Custom impact yaw scale was ignored.");
        custom.UpdateShake(.21f, 0, 0);
        Check(custom.Shake == Vector2.Zero, "Custom impact duration was ignored.");
        var disabled = new CarCamera(new() { MudAmplitude = 0, ImpactAmplitude = 0 });
        disabled.UpdateShake(.25f, 8, 1);
        disabled.TriggerImpact(20);
        Check(disabled.Shake == Vector2.Zero, "Zero amplitudes failed to disable camera shake.");

        foreach (var invalid in new CameraShakeSettings[]
        {
            new() { MudAmplitude = -1 }, new() { MudEntryDelay = float.NaN },
            new() { MudFullStrengthSpeed = 0 }, new() { MudFadeInRate = 0 },
            new() { ImpactMinSpeed = 20 }, new() { ImpactMaxDuration = .01f },
            new() { ImpactHighSpeedFrequency = float.PositiveInfinity }
        })
        {
            var rejected = false;
            try { invalid.Validate(); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Invalid camera shake settings were accepted.");
        }

        Console.WriteLine($"PASS: {count} car camera assertions (free look, recentering, views, field of view, wall clearance, mud and impact shake).");
        return count;
    }

    private static RenderMap Map((int X, int Y, RenderCellFlags Flags)? obstacle = null)
    {
        const int size = 15;
        var cells = new RenderCell[size * size];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var flags = x == 0 || y == 0 || x == size - 1 || y == size - 1
                ? RenderCellFlags.Wall | RenderCellFlags.StopsRay
                : RenderCellFlags.SkyCeiling;
            cells[y * size + x] = new(0, 0, 0, flags);
        }
        if (obstacle is { } block) cells[block.Y * size + block.X] = new(0, 0, 0, block.Flags);
        return new(size, size, cells, [], [], new TextureCatalog([]), 1);
    }
}
