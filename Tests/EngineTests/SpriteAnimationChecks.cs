using System.Numerics;
using System.Text.Json;
using WolfSlop.Engine.World;

internal static class SpriteAnimationChecks
{
    public static int Run()
    {
        var assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception(message);
        }
        void Reject(SpriteDefinition definition)
        {
            try { Compile(definition); }
            catch (InvalidDataException) { assertions++; return; }
            throw new Exception("Invalid decorative animation was accepted.");
        }

        var definition = new SpriteDefinition
        {
            Id = 2, PassageId = 0, RaycastTransparent = true, Collides = false,
            Texture = "textures/animation-test.png", TextureLayer = 8,
            Atlas = new SpriteAtlas
            {
                Frames = [
                    new() { X = 0, Y = 0, Width = 10, Height = 10 },
                    new() { X = 10, Y = 0, Width = 20, Height = 10 },
                    new() { X = 30, Y = 0, Width = 30, Height = 10 }
                ]
            },
            Animation = new SpriteAnimation { FramesPerSecond = 4 }
        };
        var map = Compile(definition);
        var sprite = map.Sprites.Single();
        var observer = sprite.Position + Vector2.UnitX;
        Check(sprite.Animation == definition.Animation, "Standalone sprite lost its animation definition.");
        foreach (var (seconds, expected) in new[] { (0d, 0), (.249, 0), (.25, 1), (.499, 1), (.5, 2), (.749, 2), (.75, 0), (10.5, 0) })
        {
            Check(sprite.ViewFrame(observer, seconds) == expected && sprite.ViewLayer(observer, seconds) == 8 + expected,
                $"Decorative animation selected the wrong frame at {seconds} seconds.");
            Check(sprite.ViewScale(observer, seconds) == new Vector2(expected + 1, 1),
                "Animated frame and aspect ratio selected different atlas frames.");
        }
        Check(map.Textures.Frames.Keys.Order().SequenceEqual(new uint[] { 8, 9, 10 }),
            "Animation texture frames were pruned from the scene.");

        // Sample the same absolute times after different numbers of render callbacks.
        foreach (var fps in new[] { 30, 60, 144 })
        {
            for (var tick = 0; tick <= fps * 5; tick++)
                _ = sprite.ViewLayer(observer, tick / (double)fps);
            Check(sprite.ViewFrame(observer, 5) == 2 && sprite.ViewFrame(observer, .25) == 1,
                "Decorative playback depends on render frequency or prior frame calls.");
        }
        var phased = sprite with { Animation = definition.Animation with { PhaseSeconds = 1 } };
        Check(phased.ViewFrame(observer, 0) == 1 && phased.ViewFrame(observer, .5) == 0,
            "Decorative animation phase failed to wrap across a full cycle.");
        Check(sprite.ViewFrame(observer, 86_400) == 0,
            "Decorative animation did not wrap after a long scene duration.");
        foreach (var invalidTime in new[] { -1d, double.NaN, double.PositiveInfinity })
        {
            try { sprite.ViewFrame(observer, invalidTime); }
            catch (ArgumentOutOfRangeException) { assertions++; continue; }
            throw new Exception("Invalid animation sample time was accepted.");
        }

        var grouped = Compile(definition with { PlaneAxis = "x" }, group: true).Sprites.Single();
        Check(grouped.Animation == definition.Animation && grouped.Scale == new Vector2(2, 3) &&
              grouped.Kind == (SpriteKind.FixedPlane | SpriteKind.PlaneNormalX),
            "Sprite group lost animation, scale, or fixed-plane geometry.");
        Check(grouped.ViewLayer(observer, .5) == 10 && grouped.ViewScale(observer, .5) == new Vector2(6, 3) &&
              grouped.ViewKind(observer) == (uint)grouped.Kind,
            "Animated grouped sprite geometry changed during playback.");

        var staticSprite = sprite with { Animation = null };
        Check(staticSprite.ViewLayer(observer, 500) == sprite.TextureLayer && staticSprite.ViewScale(observer, 500) == Vector2.One,
            "An atlas without animation no longer holds its first frame.");
        var directional = staticSprite with { Atlas = definition.Atlas with { Directions = 3 }, FacingCamera = false };
        foreach (var direction in new[] { Vector2.UnitX, Vector2.UnitY, -Vector2.UnitX, -Vector2.UnitY })
        {
            var view = sprite.Position + direction;
            Check(directional.ViewLayer(view, 50) == directional.ViewLayer(view) &&
                  directional.ViewScale(view, 50) == directional.ViewScale(view),
                "Decorative animation changed existing directional sprite selection.");
        }
        var ordinary = sprite with { Atlas = null, Animation = null };
        Check(ordinary.ViewLayer(observer, 50) == ordinary.TextureLayer && ordinary.ViewScale(observer, 50) == ordinary.Scale,
            "Decorative animation changed ordinary/player sprite selection.");

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize<MapObjectDefinition>(definition, jsonOptions);
        Check(JsonSerializer.Deserialize<MapObjectDefinition>(json, jsonOptions) is SpriteDefinition roundTrip &&
              roundTrip.Animation == definition.Animation && roundTrip.Atlas!.Frames.Length == 3,
            "Animation did not survive object JSON round-trip.");
        Reject(definition with { Atlas = null });
        Reject(definition with { Footprint = new() { Length = 1.5f, Width = .6f } });
        var footprintDefinition = definition with
        {
            Atlas = definition.Atlas! with { Directions = 3 }, Animation = null,
            Footprint = new() { Length = 1.5f, Width = .6f }
        };
        foreach (var invalidSize in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            Reject(footprintDefinition with { Footprint = new() { Length = invalidSize, Width = .6f } });
            Reject(footprintDefinition with { Footprint = new() { Length = 1.5f, Width = invalidSize } });
        }
        Reject(definition with { Atlas = definition.Atlas with { Directions = 3 } });
        foreach (var invalidFps in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Reject(definition with { Animation = definition.Animation with { FramesPerSecond = invalidFps } });
        foreach (var invalidPhase in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            Reject(definition with { Animation = definition.Animation with { PhaseSeconds = invalidPhase } });

        Console.WriteLine($"PASS: {assertions} decorative animation assertions (time, wrapping, groups, atlas selection, and validation).");
        return assertions;
    }

    private static WolfSlop.Engine.Graphics.RenderMap Compile(SpriteDefinition sprite, bool group = false)
    {
        var objects = new Dictionary<int, MapObjectDefinition>
        {
            [0] = new PassageDefinition { Id = 0, RaycastTransparent = true, Collides = false },
            [1] = new WallDefinition { Id = 1, PassageId = 0, RaycastTransparent = false, Collides = true },
            [2] = sprite,
            [3] = new SpriteGroupDefinition
            {
                Id = 3, PassageId = 0, RaycastTransparent = true, Collides = false,
                Sprites = [new() { SpriteId = 2, Offset = new() { X = .2f, Y = -.1f }, Scale = new() { X = 2, Y = 3 } }]
            }
        };
        var map = new MapDefinition
        {
            WorldMap = [[1, 1, 1, 1, 1], [1, 0, 0, 0, 1], [1, 0, group ? 3 : 2, 0, 1], [1, 0, 0, 0, 1], [1, 1, 1, 1, 1]],
            Spawn = new() { X = 1.5f, Y = 1.5f, DirectionX = 1, DirectionY = 0, MoveSpeed = 2, RotSpeed = 2 }
        };
        return new MapCompiler(objects, 8, 8).Compile(map, 1).Renderer;
    }
}
