using System.Numerics;
using System.Text.Json;
using WolfNET.Engine.Graphics;

internal static class SpriteSelectionFixtures
{
    public static void Write(string root)
    {
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
        Console.WriteLine($"Generated independent sprite references: {orderingCases.Count} ordering cases, {views.Count} synthetic views.");
    }
}
