using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using WolfNET.Engine.Graphics;

internal static class SpriteSelectionFixtures
{
    public static void Write(string root)
    {
        ValidateUploadLayout();
        var random = new Random(381);
        Sprite Make(float x, float y, uint id, float width = 1) => new(new(x, y), 10, id) { Scale = new(width, 1) };
        var orderingCases = new List<object>();
        foreach (var count in new[] { 1, 2, 3, 15, 16, 17, 31, 64, 257, 1024, 2048 })
        {
            var input = Enumerable.Range(0, count).Select(i => Make(random.Next(-20, 21), random.Next(-20, 21), (uint)i)).ToArray();
            var expected = input.Select(sprite => (Sprite: sprite, Distance: Vector2.DistanceSquared(Vector2.Zero, sprite.Position))).ToList();
            expected.Sort(static (a, b) => b.Distance.CompareTo(a.Distance));
            orderingCases.Add(new
            {
                distances = input.Select(sprite => Vector2.DistanceSquared(Vector2.Zero, sprite.Position)).ToArray(),
                expected = expected.Select(view => view.Sprite.OwnerCell).ToArray()
            });
        }

        var sprites = Enumerable.Range(0, 4096)
            .Select(i => Make(i % 64 * 8, i / 64 * 8, (uint)i, 1 + i % 4)).ToArray();
        var positions = sprites.SelectMany(sprite => new[] { sprite.Position.X, sprite.Position.Y, sprite.Scale.X, sprite.MaximumViewWidth }).ToArray();
        var selector = new ReferenceSpriteSelection();
        var views = new List<object>();
        for (var i = 0; i < 25; i++)
        {
            var position = new Vector2(80 + i % 5 * 80, 80 + i / 5 * 80);
            var angle = i * MathF.Tau / 25;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            selector.Select(sprites, position, direction, new(0, -1), 60, true, 0);
            var visible = selector.Visible.Select(item => item.Order).ToArray();
            selector.Select(sprites, position, direction, new(0, -1), 60, false, 0);
            views.Add(new
            {
                x = position.X, y = position.Y, directionX = direction.X, directionY = direction.Y,
                widths = Array.Empty<float[]>(), visible, expected = selector.Visible.Select(item => item.Order).ToArray()
            });
        }
        var directory = Path.Combine(root, "artifacts", "performance");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "sprite-sort-oracle.json"), JsonSerializer.Serialize(orderingCases));
        File.WriteAllText(Path.Combine(directory, "sprite-spatial-oracle.json"), JsonSerializer.Serialize(new { positions, views }));
        WriteGpuReference(directory);
        Console.WriteLine($"Generated independent sprite references: {orderingCases.Count} ordering cases, {views.Count} synthetic views.");
    }

    private static void ValidateUploadLayout()
    {
        var selector = new WolfNET.Engine.Browser.Graphics.SpriteSelection();
        Sprite[] scenery = [new(new(2, 3), 10, 42) { Scale = new(4, 5), Elevation = .25f, Kind = SpriteKind.PlaneNormalX }];
        Sprite[] actors = [new(new(-2, -3), 20, 0) { Scale = new(6, 7), Elevation = -.5f, SwapRedBlue = true }];
        selector.Resolve(scenery, [2, 9, 1, 0, 0, 0], 2, [1, 0], [8, 9], actors);
        var words = MemoryMarshal.Cast<WolfNET.Engine.Browser.Graphics.SpriteSelection.VisibleSprite, uint>(selector.Visible);
        uint F(float value) => BitConverter.SingleToUInt32Bits(value);
        uint[] expected = [F(-2), F(-3), 20, 0, F(9), F(7), F(-.5f), 32,
            F(2), F(3), 11, 42, F(8), F(5), F(.25f), (uint)SpriteKind.PlaneNormalX];
        if (!words.SequenceEqual(expected) || selector.CandidatesExamined != 9)
            throw new Exception("Selected scenery/actors must retain sort order and the exact 32-byte shader layout.");
        selector.Clear();
        if (selector.Visible.Length != 0) throw new Exception("Scene release retained visible sprites.");
    }

    private static void WriteGpuReference(string directory)
    {
        var directional = new SpriteAtlas { Directions = 8,
            Frames = Enumerable.Range(0, 8).Select(i => new AtlasFrame { X = i * 16, Y = 0, Width = 8 + i, Height = 12 }).ToArray() };
        var animated = new SpriteAtlas { Frames = [new() { X = 0, Y = 0, Width = 10, Height = 10 },
            new() { X = 10, Y = 0, Width = 30, Height = 10 }, new() { X = 40, Y = 0, Width = 10, Height = 20 }] };
        var sprites = Enumerable.Range(0, 81).Select(i => new Sprite(new((i % 9 - 4) * 9, (i / 9 - 4) * 9), (uint)(10 + i * 8), (uint)i + 1)
        {
            Scale = new(1 + i % 3, 1.5f), Elevation = (i % 5 - 2) * .1f,
            Atlas = i % 3 == 0 ? directional : i % 3 == 1 ? animated : null,
            Animation = i % 3 == 1 ? new SpriteAnimation { FramesPerSecond = 4, PhaseSeconds = i * .137f } : null,
            RotationDegrees = i * 17 - 270, FacingCamera = i % 2 == 0,
            Footprint = i % 6 == 0 ? new SpriteFootprint { Width = 1, Length = 3 } : null,
            Kind = i % 5 == 0 ? SpriteKind.PlaneNormalX : SpriteKind.Billboard,
            SwapRedBlue = i % 4 == 0, SwapRedGreen = i % 7 == 0
        }).ToArray();
        var (records, atlases) = WolfNET.Engine.Browser.Graphics.SpriteSelection.PrepareGpuScene(sprites);
        var positions = sprites.SelectMany((sprite, i) => new[] { sprite.Position.X, sprite.Position.Y, records[i].Scale.X, sprite.MaximumViewWidth }).ToArray();
        var views = new List<object>();
        var reference = new ReferenceSpriteSelection();
        for (var i = 0; i < 48; i++)
        {
            var observer = new Vector2((i % 5 - 2) * 2, (i % 7 - 3) * 2);
            var angle = i * .51f;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var plane = new Vector2(direction.Y, -direction.X) * .57735026f;
            var time = i * .2499;
            Sprite[] actors = [new(new(3 + i * .1f, 2), 900, 0) { Atlas = directional, FacingCamera = false,
                Footprint = new() { Width = 1, Length = 2 }, SwapRedBlue = true },
                new(new(-2, 1 + i * .1f), 950, 0) { Atlas = animated, Animation = new() { FramesPerSecond = 4 }, SwapRedGreen = true }];
            var actorWords = new uint[actors.Length * 12];
            for (var actor = 0; actor < actors.Length; actor++)
            {
                var sprite = actors[actor]; var frame = sprite.ViewFrame(observer, time);
                WolfNET.Engine.Browser.Graphics.SpriteSelection.VisibleSprite[] record = [new(sprite.Position,
                    sprite.TextureLayer + (uint)frame, sprite.OwnerCell, new(sprite.ViewWidth(frame), sprite.Scale.Y), sprite.Elevation, sprite.ViewKind(frame))];
                MemoryMarshal.Cast<WolfNET.Engine.Browser.Graphics.SpriteSelection.VisibleSprite, uint>(record.AsSpan()).CopyTo(actorWords.AsSpan(actor * 12));
                actorWords[actor * 12 + 8] = BitConverter.SingleToUInt32Bits(sprite.MaximumViewWidth);
            }
            reference.Select(sprites.Concat(actors).ToArray(), observer, direction, plane, 40, i % 2 == 0, time);
            var expected = reference.Visible.Select(view => new WolfNET.Engine.Browser.Graphics.SpriteSelection.VisibleSprite(
                view.Sprite.Position, view.Layer, view.Sprite.OwnerCell, view.Scale, view.Sprite.Elevation, view.Kind)).ToArray();
            views.Add(new { x = observer.X, y = observer.Y, dx = direction.X, dy = direction.Y,
                plane = plane.Length(), time, cull = i % 2 == 0, actors = actorWords,
                expected = MemoryMarshal.Cast<WolfNET.Engine.Browser.Graphics.SpriteSelection.VisibleSprite, uint>(expected.AsSpan()).ToArray() });
        }
        File.WriteAllText(Path.Combine(directory, "sprite-upload-oracle.json"), JsonSerializer.Serialize(new
        {
            positions, records = MemoryMarshal.Cast<WolfNET.Engine.Browser.Graphics.SpriteSelection.VisibleSprite, uint>(records.AsSpan()).ToArray(),
            atlases, views
        }, JsonSerializerOptions.Web));
    }
}
