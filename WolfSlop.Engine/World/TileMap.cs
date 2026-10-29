using System.Net.Http.Json;
using System.Numerics;
using WolfSlop.Engine.Graphics;

namespace WolfSlop.Engine.World;

public sealed class TileMap : IMap
{
    private readonly MapCompiler _compiler;
    private MapDefinition _definition;
    private readonly DoorSystem _doors = new();
    public IReadOnlyCollection<DoorState> Doors => _doors.Doors;
    public int[][] WorldMap => Copy(_definition.WorldMap);
    public IReadOnlyDictionary<int, MapObjectDefinition> Objects { get; }
    public RenderMap RendererMap { get; private set; }
    public NavigationMap NavigationMap { get; } = new();
    public ulong Revision => RendererMap.Revision;
    public ActorStartDefinition Spawn => _definition.Spawn ?? throw new InvalidOperationException("Map has no actor spawn.");
    public bool HasSpawn => _definition.Spawn != null;
    public MusicDefinition? Music => _definition.Music;
    public IReadOnlyList<AmbientEmitter> AmbientEmitters { get; private set; } = [];

    public string GetImpactMaterial(int x, int y) => x >= 0 && x < _definition.WorldMap.Length &&
        y >= 0 && y < _definition.WorldMap[x].Length && Objects.TryGetValue(_definition.WorldMap[x][y], out var obj)
            ? obj.ImpactMaterial : "hard";

    private void RebuildAudioEmitters()
    {
        var emitters = new List<AmbientEmitter>();
        for (var x = 0; x < _definition.WorldMap.Length; x++)
        for (var y = 0; y < _definition.WorldMap[x].Length; y++)
            if (Objects[_definition.WorldMap[x][y]].AmbientSound is { } cue) emitters.Add(new(cue, new(x + .5f, y + .5f)));
        AmbientEmitters = emitters.AsReadOnly();
    }
    public Vector2 SpawnPosition => new(Spawn.X, Spawn.Y);
    public Vector2 SpawnDirection => Vector2.Normalize(new Vector2(
        Spawn.DirectionX, Spawn.DirectionY));

    public TileMap(MapDefinition definition, IReadOnlyDictionary<int, MapObjectDefinition> objects, int maxWidth, int maxHeight)
    {
        Objects = new System.Collections.ObjectModel.ReadOnlyDictionary<int, MapObjectDefinition>(
            new Dictionary<int, MapObjectDefinition>(objects));
        _compiler = new MapCompiler(Objects, maxWidth, maxHeight);
        _definition = definition with { WorldMap = Copy(definition.WorldMap),
            Metadata = definition.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()) };
        var compiled = _compiler.Compile(_definition, 1);
        RendererMap = compiled.Renderer;
        NavigationMap.Replace(compiled.Walkable, compiled.Jumpable, compiled.Crouchable, compiled.Surfaces);
        _doors.Rebuild(_definition.WorldMap, Objects);
        RendererMap = _doors.Apply(RendererMap, NavigationMap);
        RebuildAudioEmitters();
    }

    public static async Task<TileMap> LoadAsync(HttpClient http, string path, int maxWidth, int maxHeight,
        CancellationToken cancellationToken = default)
    {
        var definition = await http.GetFromJsonAsync<MapDefinition>(path, cancellationToken)
            ?? throw new InvalidDataException($"Map '{path}' is empty.");
        var objects = await MapObjectLoader.LoadAsync(http, path, definition, cancellationToken);
        return new TileMap(definition, objects, maxWidth, maxHeight);
    }

    public void SetCell(int x, int y, int objectId)
    {
        if (_definition.WorldMap[x][y] == objectId) return;
        var cells = Copy(_definition.WorldMap);
        cells[x][y] = objectId;
        ReplaceMap(cells);
    }

    public void ReplaceMap(int[][] objectIds)
    {
        var next = _definition with { WorldMap = Copy(objectIds) };
        // Compile everything before publishing anything. Failed edits preserve both maps.
        var compiled = _compiler.Compile(next, checked(Revision + 1));
        _definition = next;
        NavigationMap.Replace(compiled.Walkable, compiled.Jumpable, compiled.Crouchable, compiled.Surfaces);
        _doors.Rebuild(_definition.WorldMap, Objects);
        RendererMap = _doors.Apply(compiled.Renderer, NavigationMap);
        RebuildAudioEmitters();
    }

    public bool Interact(Vector2 position, Vector2 direction, float radius) => _doors.Interact(position, direction, radius);

    public void UpdateDoors(float milliseconds, Vector2 position, float radius)
    {
        if (!_doors.Update(milliseconds, position, radius)) return;
        RendererMap = _doors.Apply(RendererMap with { Revision = checked(Revision + 1) }, NavigationMap);
    }

    private static int[][] Copy(int[][]? cells) => cells == null ? [] :
        cells.Select(column => column == null ? null! : (int[])column.Clone()).ToArray();
}
