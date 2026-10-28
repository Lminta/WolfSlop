using System.Numerics;
using WolfNET.Engine.Graphics;
using WolfNET.Engine.Physics;

namespace WolfNET.Engine.World;

public sealed class DoorState(int x, int y, DoorDefinition definition)
{
    public int X { get; } = x;
    public int Y { get; } = y;
    public DoorDefinition Definition { get; } = definition;
    public float Openness { get; internal set; }
    public bool TargetOpen { get; internal set; }
    internal float OpenElapsedSeconds { get; set; }
}

public sealed class DoorSystem
{
    private readonly Dictionary<(int X, int Y), DoorState> _doors = [];
    public IReadOnlyCollection<DoorState> Doors => _doors.Values;

    public void Rebuild(int[][] cells, IReadOnlyDictionary<int, MapObjectDefinition> objects)
    {
        var previous = new Dictionary<(int, int), DoorState>(_doors);
        _doors.Clear();
        for (int x = 0; x < cells.Length; x++)
        for (int y = 0; y < cells[x].Length; y++)
            if (objects[cells[x][y]] is DoorDefinition definition)
                _doors[(x, y)] = previous.TryGetValue((x, y), out var old) && old.Definition == definition
                    ? old : new DoorState(x, y, definition);
    }

    public bool Interact(Vector2 position, Vector2 direction, float radius)
    {
        var tile = ((int)MathF.Floor(position.X), (int)MathF.Floor(position.Y));
        foreach (var door in _doors.Values)
        {
            if (Math.Abs(door.X - tile.Item1) + Math.Abs(door.Y - tile.Item2) != 1) continue;
            bool normalX = door.Definition.Axis == "x";
            var normal = normalX ? direction.X : direction.Y;
            if (Math.Abs(normal) < .00001f) continue;
            var distance = ((normalX ? door.X : door.Y) + .5f - (normalX ? position.X : position.Y)) / normal;
            var across = (normalX ? position.Y : position.X) + distance * (normalX ? direction.Y : direction.X) -
                (normalX ? door.Y : door.X);
            if (distance < 0 || across < 0 || across > 1) continue;
            if (door.TargetOpen && door.Definition.Collides && Occupied(door, position, radius)) return false;
            door.TargetOpen = !door.TargetOpen;
            door.OpenElapsedSeconds = 0;
            return true;
        }
        return false;
    }

    public bool Update(float milliseconds, Vector2 position, float radius)
    {
        bool changed = false;
        foreach (var door in _doors.Values)
        {
            var previous = door.Openness;
            var seconds = Math.Max(0, milliseconds) / 1000;
            var occupied = Occupied(door, position, radius);
            if (!door.TargetOpen && door.Openness > 0 && door.Definition.Collides && occupied)
            {
                door.TargetOpen = true;
                door.OpenElapsedSeconds = 0;
            }
            if (door.TargetOpen)
            {
                // Only time spent fully open counts toward the closing delay.
                var openingTime = (1 - door.Openness) * door.Definition.OpenSeconds;
                var openingStep = Math.Min(seconds, openingTime);
                door.Openness = openingStep >= openingTime ? 1 : door.Openness + openingStep / door.Definition.OpenSeconds;
                seconds -= openingStep;
                if (door.Openness == 1 && door.Definition.AutoCloseSeconds > 0)
                {
                    var waitingTime = Math.Max(0, door.Definition.AutoCloseSeconds - door.OpenElapsedSeconds);
                    var waitingStep = Math.Min(seconds, waitingTime);
                    door.OpenElapsedSeconds += waitingStep;
                    seconds -= waitingStep;
                    // An expired timer waits for the entire player circle to clear.
                    if (door.OpenElapsedSeconds >= door.Definition.AutoCloseSeconds && !occupied)
                        door.TargetOpen = false;
                }
            }
            if (!door.TargetOpen)
            {
                door.Openness = Math.Max(0, door.Openness - seconds / door.Definition.OpenSeconds);
                if (door.Openness == 0) door.OpenElapsedSeconds = 0;
            }
            changed |= door.Openness != previous;
        }
        return changed;
    }

    private static bool Occupied(DoorState door, Vector2 point, float radius) =>
        CircleGeometry.OverlapsCell(point, radius, door.X, door.Y);

    public RenderMap Apply(RenderMap map, NavigationMap navigation)
    {
        if (_doors.Count == 0) return map;
        var cells = map.Cells.ToArray();
        var sprites = map.Sprites.ToArray();
        foreach (var door in _doors.Values)
        {
            int index = door.Y * map.Width + door.X;
            var flags = cells[index].Flags & (RenderCellFlags)0xffff;
            flags &= ~(RenderCellFlags.Wall | RenderCellFlags.StopsRay);
            if (door.Openness < 1)
            {
                flags |= RenderCellFlags.Wall;
                if (!door.Definition.RaycastTransparent) flags |= RenderCellFlags.StopsRay;
            }
            flags |= (RenderCellFlags)((uint)MathF.Round(door.Openness * 65535) << 16);
            cells[index] = cells[index] with { Flags = flags };
            navigation.SetWalkable(door.X, door.Y, !door.Definition.Collides || door.Openness >= 1);
        }
        for (int i = 0; i < sprites.Length; i++)
        {
            var sprite = sprites[i];
            if ((sprite.Kind & (SpriteKind)3) == 0 || sprite.Kind.HasFlag(SpriteKind.FixedPlane)) continue;
            int index = (int)sprite.OwnerCell - 1;
            if (!_doors.TryGetValue((index % map.Width, index / map.Width), out var door)) continue;
            var axis = door.Definition.Axis == "x" ? Vector2.UnitY : Vector2.UnitX;
            sprites[i] = sprite with { Position = new Vector2(door.X + .5f, door.Y + .5f) + axis * (door.Openness * .5f),
                Scale = new(1 - door.Openness, 1) };
        }
        return map with { Cells = Array.AsReadOnly(cells), Sprites = Array.AsReadOnly(sprites) };
    }
}
