using System.Numerics;
using WolfNET.Engine.World;
using WolfNET.Engine.Core;
using WolfNET.Engine.Input;
using WolfNET.Engine.Graphics;

internal static class EngineChecks
{
    public static int Run()
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception(message);
        }
        void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { assertions++; return; }
            throw new Exception($"Expected {typeof(T).Name}.");
        }

        var keyboard = new KeyboardState();
        keyboard.Press("KeyW");
        keyboard.Press("Space");
        Check(keyboard.KeyState.Forward && keyboard.KeyState.Jump && keyboard.ConsumeJump(), "Keyboard lost held input or jump edge.");
        keyboard.Press("Space", repeat: true);
        keyboard.Press("Space");
        Check(!keyboard.ConsumeJump(), "Held Space repeated a jump.");
        keyboard.Release("Space");
        keyboard.Press("Space");
        keyboard.Release("Space");
        Check(keyboard.ConsumeJump() && !keyboard.KeyState.Jump, "A press and release between frames lost the jump.");
        keyboard.Press("AltLeft");
        keyboard.Press("AltRight");
        keyboard.Release("AltLeft");
        Check(keyboard.KeyState.Crouch, "Releasing one Alt key cleared the other.");
        keyboard.Release("AltRight");
        Check(!keyboard.KeyState.Crouch, "Crouch remained active after both Alt keys were released.");
        keyboard.Press("KeyV");
        keyboard.Release("KeyV");
        keyboard.Press("KeyV");
        Check(!keyboard.ConsumeViewToggle(), "Two view toggles in one frame did not cancel.");
        keyboard.Release("KeyV");
        keyboard.Press("KeyV");
        keyboard.Press("KeyF");
        keyboard.Press("Space");
        keyboard.Reset();
        keyboard.Press("KeyW", repeat: true);
        Check(!keyboard.KeyState.Forward && !keyboard.ConsumeJump() && !keyboard.ConsumeViewToggle() && !keyboard.ConsumeAction(),
            "Capture loss retained input or allowed repeat to reactivate movement.");
        keyboard.Press("KeyF");
        Check(keyboard.ConsumeAction() && !keyboard.ConsumeAction(), "Interact was not consumed exactly once.");
        keyboard.Reset();

        var timing = new FrameTiming(60);
        Check(timing.TryAdvance(0, out var elapsed) && elapsed == 0, "First frame was delayed.");
        Check(!timing.TryAdvance(8, out _), "FPS limiter accepted an early frame.");
        Check(timing.TryAdvance(17, out elapsed) && elapsed == 17, "Frame delta included skipped browser callbacks.");
        Check(timing.TryAdvance(1000, out elapsed) && elapsed == 100, "Long frame was not clamped.");
        Check(!timing.TryAdvance(1000, out _), "A missed deadline caused a catch-up frame.");
        timing.Reset(5000);
        Check(timing.TryAdvance(5000, out elapsed) && elapsed == 0, "Reset included loading time in simulation.");
        timing.Reset();
        for (var tick = 0; tick < 240; tick++)
            Check(timing.TryAdvance(tick * (1000d / 120), out _) == (tick % 2 == 0),
                "Browser timestamps did not maintain an even 60 FPS cadence on a 120 Hz display.");
        Throws<ArgumentOutOfRangeException>(() => new FrameTiming(0));
        Throws<ArgumentException>(() => new RendererSettings(targetFPS: 0).Validate());
        Throws<ArgumentException>(() => new RendererSettings { FovDegrees = float.NaN }.Validate());
        Throws<ArgumentException>(() => new RendererSettings { MaxAnisotropy = 17 }.Validate());
        Check(new RendererSettings().ColumnInterlaceFactor == 1, "Column interlacing must be off by default.");
        foreach (var factor in new[] { 1, 2, 3 })
        {
            var settings = System.Text.Json.JsonSerializer.Deserialize<RendererSettings>(
                $"{{\"columnInterlaceFactor\":{factor}}}", new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            settings.Validate();
            Check(settings.ColumnInterlaceFactor == factor, "Graphics JSON did not load columnInterlaceFactor.");
        }
        foreach (var invalidFactor in new[] { -1, 0, 4, int.MaxValue })
            Throws<ArgumentException>(() => new RendererSettings { ColumnInterlaceFactor = invalidFactor }.Validate());
        Check(new RendererSettings().RenderDistance == 60 && new RendererSettings().FogStartDistance == 40,
            "Default fog must start at forty map squares and end at sixty.");
        var distanceSettings = System.Text.Json.JsonSerializer.Deserialize<RendererSettings>(
            "{\"renderDistance\":72,\"fogStartDistance\":52}", new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        distanceSettings.Validate();
        Check(distanceSettings.RenderDistance == 72 && distanceSettings.FogStartDistance == 52,
            "Graphics JSON did not load renderDistance and fogStartDistance.");
        foreach (var invalidDistance in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Throws<ArgumentException>(() => new RendererSettings { RenderDistance = invalidDistance }.Validate());
        foreach (var invalidStart in new[] { -1f, 60f, 61f, float.NaN, float.PositiveInfinity })
            Throws<ArgumentException>(() => new RendererSettings { FogStartDistance = invalidStart }.Validate());
        new RendererSettings { FogStartDistance = 0 }.Validate();

        var owner = new TestObject();
        var other = new TestObject();
        var component = new ProbeComponent(owner);
        owner.AddComponent(component);
        owner.AddComponent(component);
        Throws<ArgumentException>(() => other.AddComponent(component));
        var objects = new ObjectCollection();
        objects.Add(owner);
        objects.Add(owner);
        objects.Update(16);
        Check(component.Updates == 1 && component.Elapsed == 16, "Object/component registration caused duplicate updates.");
        objects.Dispose();
        objects.Dispose();
        Check(component.Disposals == 1 && owner.Components.Count == 0, "Owned components were not disposed exactly once.");
        Throws<ObjectDisposedException>(() => objects.Update(16));
        Throws<ObjectDisposedException>(() => owner.AddComponent(new ProbeComponent(owner)));
        other.Dispose();

        // Editing must apply current door state to the newly compiled map, including resizes.
        var editMap = MakeMap(9, 9);
        Check(editMap.Interact(new(4.5f), Vector2.UnitX, .15f), "Door setup failed.");
        editMap.UpdateDoors(1000, new(4.5f), .15f);
        editMap.SetCell(2, 2, 1);
        Check(editMap.RendererMap.Cells[4 * 9 + 5].Flags.HasFlag(RenderCellFlags.Door) &&
              !editMap.RendererMap.Cells[4 * 9 + 5].Flags.HasFlag(RenderCellFlags.StopsRay) &&
              editMap.RendererMap.Sprites.Single().Scale.X == 0 && editMap.NavigationMap.IsWalkable(5, 4),
            "A map edit reset the open door's render state.");
        editMap.ReplaceMap(MakeMap(12, 10, doorX: 10).WorldMap);
        Check(editMap.RendererMap.Width == 12 && editMap.RendererMap.Sprites.Single().Position.X == 10.5f,
            "Resizing applied door state to stale render cells.");

        Console.WriteLine($"PASS: {assertions} engine assertions (input, timing, ownership, and door edits).");
        return assertions;
    }

    private static TileMap MakeMap(int width, int height, int doorX = 5)
    {
        var cells = Enumerable.Range(0, width).Select(x => Enumerable.Range(0, height)
            .Select(y => x == 0 || y == 0 || x == width - 1 || y == height - 1 ? 1 : 0).ToArray()).ToArray();
        cells[doorX][4] = 2;
        var definition = new MapDefinition
        {
            WorldMap = cells,
            Spawn = new ActorStartDefinition { X = 4.5f, Y = 4.5f, DirectionX = 1, DirectionY = 0, MoveSpeed = 2, RotSpeed = 2 }
        };
        return new TileMap(definition, new Dictionary<int, MapObjectDefinition>
        {
            [0] = new PassageDefinition { Id = 0, RaycastTransparent = true, Collides = false },
            [1] = new WallDefinition { Id = 1, PassageId = 0, RaycastTransparent = false, Collides = true },
            [2] = new DoorDefinition { Id = 2, PassageId = 0, Axis = "x", RaycastTransparent = false, Collides = true }
        }, 64, 64);
    }

    private sealed class ProbeComponent(ObjectBase parent) : ComponentBase(parent), IDisposable
    {
        public int Updates { get; private set; }
        public float Elapsed { get; private set; }
        public int Disposals { get; private set; }
        public override void Update(float deltaTime) { Updates++; Elapsed += deltaTime; }
        public void Dispose() => Disposals++;
    }
}
