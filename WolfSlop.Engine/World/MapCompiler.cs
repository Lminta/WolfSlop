using System.Numerics;
using WolfSlop.Engine.Graphics;

namespace WolfSlop.Engine.World;

public sealed class MapCompiler
{
    private sealed record SpriteTemplate(Vector3 Offset, Vector2 Scale, uint Layer, SpriteAtlas? Atlas, float RotationDegrees, SpriteKind Kind = SpriteKind.Billboard, bool FacingCamera = true, SpriteAnimation? Animation = null, string? InteractionTarget = null, string? Tag = null, SpriteFootprint? Footprint = null);
    private sealed record CompiledObject(RenderCell Cell, bool Walkable, bool Jumpable, bool Crouchable, string Surface, SpriteTemplate[] Sprites);

    private readonly Dictionary<int, CompiledObject> _objects = [];
    private readonly WallMaterial[] _materials;
    private readonly TextureCatalog _textures;
    private TextureCatalog? _sceneTextures;
    private readonly int _maxWidth;
    private readonly int _maxHeight;

    public MapCompiler(IReadOnlyDictionary<int, MapObjectDefinition> objects, int maxWidth, int maxHeight)
    {
        if (objects.Values.Any(obj => string.IsNullOrWhiteSpace(obj.ImpactMaterial) ||
            obj.AmbientSound is { } sound && string.IsNullOrWhiteSpace(sound)))
            throw new InvalidDataException("Object audio identifiers must be nonempty.");
        _maxWidth = maxWidth;
        _maxHeight = maxHeight;
        var layers = new Dictionary<uint, string?>();
        var paths = new Dictionary<string, uint>(StringComparer.Ordinal);
        var frames = new Dictionary<uint, AtlasFrame>();
        static string? Path(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Replace('\\', '/');

        // Explicit sprite slots are reserved before assigning surface textures.
        foreach (var sprite in objects.Values.OfType<SpriteDefinition>().OrderBy(sprite => sprite.Id))
        {
            var atlas = sprite.Atlas;
            if (sprite.Footprint is { } footprint && (atlas is not { Directions: > 1 } || sprite.Animation != null ||
                !float.IsFinite(footprint.Length) || !float.IsFinite(footprint.Width) || footprint.Length <= 0 || footprint.Width <= 0))
                throw new InvalidDataException($"Sprite {sprite.Id} footprint requires positive finite dimensions and a directional atlas without animation.");
            if (sprite.InteractionTarget is { } target && string.IsNullOrWhiteSpace(target))
                throw new InvalidDataException($"Sprite {sprite.Id} interactionTarget must be nonempty.");
            if (!float.IsFinite(sprite.RotationDegrees))
                throw new InvalidDataException($"Sprite {sprite.Id} has an invalid rotationDegrees.");
            if (sprite.PlaneAxis is not (null or "x" or "y"))
                throw new InvalidDataException($"Sprite {sprite.Id} planeAxis must be x/y or null.");
            if (sprite.PlaneAxis != null && atlas is { Directions: > 1 })
                throw new InvalidDataException($"Sprite {sprite.Id} cannot combine a fixed plane with directional views.");
            if (atlas != null && (atlas.Frames == null || atlas.Frames.Length == 0 || atlas.Frames.Length > 256 ||
                atlas.Directions < 1 || atlas.Directions > atlas.Frames.Length ||
                atlas.Frames.Any(frame => frame == null || frame.X < 0 || frame.Y < 0 || frame.Width <= 0 || frame.Height <= 0 ||
                    (long)frame.X + frame.Width > int.MaxValue || (long)frame.Y + frame.Height > int.MaxValue)))
                throw new InvalidDataException($"Sprite {sprite.Id} has invalid atlas frames/directions.");
            if (sprite.Animation is { } animation &&
                (atlas is not { Directions: 1 } || !float.IsFinite(animation.FramesPerSecond) || animation.FramesPerSecond <= 0 ||
                 !float.IsFinite(animation.PhaseSeconds) || animation.PhaseSeconds < 0))
                throw new InvalidDataException($"Sprite {sprite.Id} animation requires a single-direction atlas, a positive finite framesPerSecond, and a finite nonnegative phaseSeconds.");
            var count = atlas?.Frames.Length ?? 1;
            if ((ulong)sprite.TextureLayer + (uint)count > TextureCatalog.MaxLayers)
                throw new InvalidDataException($"Sprite {sprite.Id} texture layers must fit within {TextureCatalog.MaxLayers} slots.");
            var path = Path(sprite.Texture);
            for (uint i = 0; i < count; i++)
            {
                var layer = sprite.TextureLayer + i;
                var frame = atlas?.Frames[i];
                if (layers.TryGetValue(layer, out var previous) &&
                    (previous != path || frames.GetValueOrDefault(layer) != frame))
                    throw new InvalidDataException($"Conflicting textures/atlas frames in layer {layer}.");
                layers[layer] = path;
                if (frame != null) frames[layer] = frame;
            }
            if (path != null && atlas == null)
                paths[path] = paths.TryGetValue(path, out var oldLayer) ? Math.Min(oldLayer, sprite.TextureLayer) : sprite.TextureLayer;
        }

        uint nextLayer = 0;
        uint Resolve(string? texture)
        {
            var path = Path(texture);
            if (path == null) return TextureCatalog.MissingLayer;
            if (paths.TryGetValue(path, out var existing)) return existing;
            while (layers.ContainsKey(nextLayer)) nextLayer = checked(nextLayer + 1);
            if (nextLayer >= TextureCatalog.MaxLayers)
                throw new InvalidDataException($"Scene textures exceed {TextureCatalog.MaxLayers} array layers.");
            var layer = nextLayer;
            layers.Add(layer, path);
            paths.Add(path, layer);
            return layer;
        }

        PassageDefinition Passage(int ownerId, int passageId) =>
            objects.TryGetValue(passageId, out var passage) && passage is PassageDefinition typed
                ? typed : throw new InvalidDataException($"Object {ownerId} references unknown/non-passage ID {passageId}.");

        var materials = new List<WallMaterial>
        {
            new(TextureCatalog.MissingLayer, TextureCatalog.MissingLayer, TextureCatalog.MissingLayer, TextureCatalog.MissingLayer)
        };
        static SpriteKind Geometry(SpriteDefinition sprite) => sprite.PlaneAxis switch
        {
            "x" => SpriteKind.FixedPlane | SpriteKind.PlaneNormalX,
            "y" => SpriteKind.FixedPlane | SpriteKind.PlaneNormalY,
            _ => SpriteKind.Billboard
        };
        foreach (var (id, definition) in objects.OrderBy(pair => pair.Key))
        {
            if (id < 0 || id != definition.Id)
                throw new InvalidDataException($"Invalid object ID {id}.");
            uint floor = TextureCatalog.MissingLayer, ceiling = TextureCatalog.MissingLayer, material = 0;
            var flags = definition.RaycastTransparent ? RenderCellFlags.None : RenderCellFlags.StopsRay;
            PassageDefinition? passage = null;
            SpriteTemplate[] sprites = [];
            switch (definition)
            {
                case PassageDefinition value:
                    passage = value;
                    break;
                case WallDefinition wall:
                    passage = Passage(id, wall.PassageId);
                    flags |= RenderCellFlags.Wall;
                    material = (uint)materials.Count;
                    materials.Add(new WallMaterial(Resolve(wall.Textures?.North), Resolve(wall.Textures?.East),
                        Resolve(wall.Textures?.South), Resolve(wall.Textures?.West)));
                    break;
                case DoorDefinition door:
                    if (!float.IsFinite(door.AutoCloseSeconds))
                        throw new InvalidDataException($"Door {id} autoCloseSeconds must be finite.");
                    if (door.Axis is not ("x" or "y") || !float.IsFinite(door.OpenSeconds) || door.OpenSeconds <= 0)
                        throw new InvalidDataException($"Door {id} needs axis x/y and a positive finite openSeconds.");
                    passage = Passage(id, door.PassageId);
                    flags |= RenderCellFlags.Wall | RenderCellFlags.Door;
                    if (door.Axis == "x") flags |= RenderCellFlags.DoorNormalX;
                    material = (uint)materials.Count;
                    var doorLayer = Resolve(door.Texture);
                    materials.Add(new WallMaterial(doorLayer, doorLayer, doorLayer, doorLayer));
                    var kind = (door.Axis == "x" ? SpriteKind.DoorNormalX : SpriteKind.DoorNormalY) |
                        (door.RaycastTransparent ? 0 : SpriteKind.Opaque);
                    sprites = [new(Vector3.Zero, Vector2.One, doorLayer, null, 0, kind)];
                    break;
                case SpriteDefinition sprite:
                    passage = Passage(id, sprite.PassageId);
                    sprites = [new(Vector3.Zero, Vector2.One, sprite.TextureLayer, sprite.Atlas, sprite.RotationDegrees, Geometry(sprite), sprite.FacingCamera, sprite.Animation, sprite.InteractionTarget, sprite.Tag, sprite.Footprint)];
                    break;
                case SpriteGroupDefinition group:
                    passage = Passage(id, group.PassageId);
                    if (group.Sprites == null)
                        throw new InvalidDataException($"Sprite group {id} has no sprite list.");
                    sprites = group.Sprites.Select(member =>
                    {
                        if (member?.Offset == null || !float.IsFinite(member.Offset.X) || !float.IsFinite(member.Offset.Y) || !float.IsFinite(member.Offset.Z))
                            throw new InvalidDataException($"Sprite group {id} has an invalid offset.");
                        if (member.Scale == null || !float.IsFinite(member.Scale.X) || !float.IsFinite(member.Scale.Y) || member.Scale.X < 0 || member.Scale.Y < 0)
                            throw new InvalidDataException($"Sprite group {id} has an invalid scale. Multipliers must be finite and nonnegative.");
                        if (!objects.TryGetValue(member.SpriteId, out var child) || child is not SpriteDefinition sprite)
                            throw new InvalidDataException($"Sprite group {id} references unknown/non-sprite ID {member.SpriteId}.");
                        return new SpriteTemplate(new Vector3(member.Offset.X, member.Offset.Y, member.Offset.Z),
                            new Vector2(member.Scale.X, member.Scale.Y), sprite.TextureLayer, sprite.Atlas, sprite.RotationDegrees, Geometry(sprite), sprite.FacingCamera, sprite.Animation, sprite.InteractionTarget, sprite.Tag, sprite.Footprint);
                    }).ToArray();
                    break;
                default:
                    throw new InvalidDataException($"Unsupported object type for ID {id}.");
            }
            if (passage != null)
            {
                if (string.IsNullOrWhiteSpace(passage.Surface))
                    throw new InvalidDataException($"Passage {passage.Id} surface must have a name.");
                floor = Resolve(passage.FloorTexture);
                if (passage.CeilingMode is not ("texture" or "sky"))
                    throw new InvalidDataException($"Passage {passage.Id} ceilingMode must be texture or sky.");
                if (passage.CeilingMode == "sky") flags |= RenderCellFlags.SkyCeiling;
                else ceiling = Resolve(passage.CeilingTexture);
            }
            _objects.Add(id, new CompiledObject(new RenderCell(material, floor, ceiling, flags), !definition.Collides, definition.Jumpable, definition.Crouchable, passage?.Surface ?? "mud", sprites));
        }
        _materials = materials.ToArray();
        _textures = new TextureCatalog(layers, frames);
    }

    public (RenderMap Renderer, bool[][] Walkable, bool[][] Jumpable, bool[][] Crouchable, string[][] Surfaces) Compile(MapDefinition definition, ulong revision)
    {
        definition.Music?.Validate();
        if (definition.Sky is { } sky && (string.IsNullOrWhiteSpace(sky.Texture) || !float.IsFinite(sky.RotationDegrees)))
            throw new InvalidDataException("Sky needs a texture path and a finite rotationDegrees.");
        var map = definition.WorldMap;
        if (map == null || map.Length == 0 || map[0] == null || map[0].Length == 0)
            throw new InvalidDataException("Map must contain a nonempty grid.");
        int width = map.Length, height = map[0].Length;
        if (width > _maxWidth || height > _maxHeight)
            throw new InvalidDataException($"Map dimensions exceed {_maxWidth}x{_maxHeight}.");
        var cells = new RenderCell[checked(width * height)];
        var walkable = new bool[width][];
        var jumpable = new bool[width][];
        var crouchable = new bool[width][];
        var surfaces = new string[width][];
        var sprites = new List<Sprite>();
        for (int x = 0; x < width; x++)
        {
            if (map[x] == null || map[x].Length != height)
                throw new InvalidDataException("Map must be rectangular (worldMap[x][y]).");
            walkable[x] = new bool[height];
            jumpable[x] = new bool[height];
            crouchable[x] = new bool[height];
            surfaces[x] = new string[height];
            for (int y = 0; y < height; y++)
            {
                if (!_objects.TryGetValue(map[x][y], out var obj))
                    throw new InvalidDataException($"Unknown object ID {map[x][y]} at ({x}, {y}).");
                if ((x == 0 || y == 0 || x == width - 1 || y == height - 1) &&
                    (obj.Walkable || obj.Jumpable || obj.Crouchable || obj.Cell.Flags.HasFlag(RenderCellFlags.Door) || !obj.Cell.Flags.HasFlag(RenderCellFlags.StopsRay)))
                    throw new InvalidDataException("Map boundaries must block movement, jumping, crouching, and rays.");
                int index = y * width + x;
                cells[index] = obj.Cell;
                walkable[x][y] = obj.Walkable;
                jumpable[x][y] = obj.Jumpable;
                crouchable[x][y] = obj.Crouchable;
                surfaces[x][y] = obj.Surface;
                foreach (var sprite in obj.Sprites)
                {
                    var position = new Vector2(x + 0.5f + sprite.Offset.X, y + 0.5f + sprite.Offset.Y);
                    if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) ||
                        position.X < 0 || position.Y < 0 || position.X >= width || position.Y >= height)
                        throw new InvalidDataException($"Sprite from ({x}, {y}) is outside the map.");
                    sprites.Add(new Sprite(position, sprite.Layer, (uint)index + 1)
                    { Scale = sprite.Scale, Footprint = sprite.Footprint, Elevation = sprite.Offset.Z, Atlas = sprite.Atlas, Animation = sprite.Animation, InteractionTarget = sprite.InteractionTarget, Tag = sprite.Tag, RotationDegrees = sprite.RotationDegrees, Kind = sprite.Kind, FacingCamera = sprite.FacingCamera });
                }
            }
        }
        var start = definition.Spawn;
        if (start != null && (!float.IsFinite(start.X) || !float.IsFinite(start.Y) ||
            start.X < 0 || start.Y < 0 || start.X >= width || start.Y >= height || !walkable[(int)start.X][(int)start.Y]))
            throw new InvalidDataException("Actor spawn must be in a walkable map cell.");
        start?.Validate();

        var usedLayers = cells.SelectMany(cell => new[] { cell.FloorTextureLayer, cell.CeilingTextureLayer }
            .Concat(cell.Flags.HasFlag(RenderCellFlags.Wall)
                ? new[] { _materials[cell.MaterialIndex].North, _materials[cell.MaterialIndex].East,
                    _materials[cell.MaterialIndex].South, _materials[cell.MaterialIndex].West } : []))
            .Concat(sprites.SelectMany(sprite => Enumerable.Range(0, sprite.Atlas?.Frames.Length ?? 1).Select(i => sprite.TextureLayer + (uint)i))).ToHashSet();
        var layers = _textures.Layers.Where(pair => usedLayers.Contains(pair.Key)).ToDictionary();
        if (_sceneTextures == null || !_sceneTextures.Layers.OrderBy(pair => pair.Key).SequenceEqual(layers.OrderBy(pair => pair.Key)))
            _sceneTextures = new TextureCatalog(layers, _textures.Frames.Where(pair => usedLayers.Contains(pair.Key)).ToDictionary());
        if (definition.Sky == null && cells.Any(cell => cell.Flags.HasFlag(RenderCellFlags.SkyCeiling)))
            throw new InvalidDataException("A sky ceiling requires a sky configuration on the map.");
        return (new RenderMap(width, height, Array.AsReadOnly(cells), Array.AsReadOnly(_materials),
            sprites.AsReadOnly(), _sceneTextures, revision) { Sky = definition.Sky, Metadata = new System.Collections.ObjectModel.ReadOnlyDictionary<string, System.Text.Json.JsonElement>(new Dictionary<string, System.Text.Json.JsonElement>(definition.Metadata)) }, walkable, jumpable, crouchable, surfaces);
    }
}
