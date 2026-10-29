using System.Numerics;
using WolfSlop.Engine.Actors.Components;

internal static class MapChecks
{
    public static int Run()
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception(message);
        }
        void Reject(Action action, string message)
        {
            try { action(); }
            catch (InvalidDataException) { assertions++; return; }
            throw new Exception(message);
        }

        var definition = new MapDefinition
        {
            WorldMap = Enumerable.Range(0, 9).Select(x => Enumerable.Range(0, 9)
                .Select(y => x == 0 || y == 0 || x == 8 || y == 8 ? 1 : 0).ToArray()).ToArray(),
            Spawn = new() { X = 4.5f, Y = 4.5f, DirectionX = 1, DirectionY = 0, MoveSpeed = 5, RotSpeed = 2 }
        };

        new MusicDefinition().Validate();
        Reject(() => new MusicDefinition { Tracks = [" "] }.Validate(), "Music must reject empty track paths");
        Reject(() => new MusicDefinition { CrossfadeSeconds = float.NaN }.Validate(), "Music must reject invalid fade times");
        Reject(() => new MusicDefinition { Volume = 1.1f }.Validate(), "Music must reject volume above one");
        var objects = new Dictionary<int, MapObjectDefinition>
        {
            [0] = new PassageDefinition { Id = 0, RaycastTransparent = true, Collides = false },
            [1] = new WallDefinition { Id = 1, PassageId = 0, RaycastTransparent = false, Collides = true },
            [2] = new SpriteDefinition { Id = 2, PassageId = 0, RaycastTransparent = true, Collides = true,
                Texture = "textures/test.png", TextureLayer = 8 }
        };
        TileMap Compile(MapDefinition? source = null) => new(source ?? definition, objects, 64, 64);
        var map = Compile();
        var navigation = map.NavigationMap;
        var snapshot = map.RendererMap;
        map.SetCell(5, 4, 0);
        Check(ReferenceEquals(snapshot, map.RendererMap), "No-op edit rebuilt the map");
        map.SetCell(5, 4, 2);
        Check(ReferenceEquals(navigation, map.NavigationMap) && !navigation.IsWalkable(5, 4) &&
            map.RendererMap.Sprites.Count == 1, "Edit did not update navigation and rendering together");
        snapshot = map.RendererMap;
        Reject(() => map.SetCell(5, 4, 999), "Unknown object accepted");
        Reject(() => map.SetCell(0, 0, 0), "Open boundary accepted");
        Check(ReferenceEquals(snapshot, map.RendererMap), "Invalid edit partially published");
        var copy = map.WorldMap;
        copy[5][4] = 0;
        Check(!map.NavigationMap.IsWalkable(5, 4), "External grid mutation changed the map");
        map.ReplaceMap(definition.WorldMap);
        Check(navigation.IsWalkable(5, 4) && map.RendererMap.Sprites.Count == 0, "Replacement retained stale state");
        Reject(() => Compile(definition with { WorldMap = [[1], [1, 0]] }), "Ragged map accepted");
        Reject(() => Compile(definition with { Spawn = definition.Spawn with { X = 0, Y = 0 } }), "Blocked spawn accepted");
        Reject(() => Compile(definition with { Spawn = definition.Spawn with { DirectionX = 0 } }), "Zero heading accepted");
        // Exercise the real walking controller against a wall, then jump and crouch.
        using (var player = new TestObject { Transform = new() { Position = new(4.5f), Rotation = Vector2.UnitX } })
        {
            var keys = new FakeKeyboard { KeyState = new() { Forward = true } };
            var camera = new CameraComponent(player) { Plane = new(0, -.66f) };
            var controller = new WalkingController(keys, new FakeMouse(), navigation, camera, map.Spawn, player);
            for (var frame = 0; frame < 120; frame++) controller.Update(1000f / 60);
            Check(player.Transform.Position.X > 7 && player.Transform.Position.X < 8 &&
                navigation.IsWalkable((int)player.Transform.Position.X, (int)player.Transform.Position.Y),
                "Walking failed to advance or passed through the boundary wall");
            player.Transform = player.Transform with { Position = new(4.5f) };
            keys.KeyState = new() { Jump = true };
            controller.Update(0);
            controller.Update(100);
            Check(controller.InAir && camera.Elevation > 0, "Jump did not raise the player");
            for (var frame = 0; frame < 60; frame++) controller.Update(1000f / 60);
            Check(!controller.InAir && camera.Elevation == 0, "Jump did not land");
            keys.KeyState = new() { Crouch = true };
            controller.Update(16);
            Check(controller.Crouching && camera.Elevation < 0, "Crouch did not lower the camera");
            keys.KeyState = new();
            controller.Update(16);
            Check(!controller.Crouching && camera.Elevation == 0, "Crouch did not release");
        }

        return assertions;
    }
}
