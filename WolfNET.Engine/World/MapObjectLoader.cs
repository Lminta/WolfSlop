using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Text.Json;

namespace WolfNET.Engine.World;

public static class MapObjectLoader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true
    };

    public static async Task<IReadOnlyDictionary<int, MapObjectDefinition>> LoadAsync(
        HttpClient http, string mapPath, MapDefinition map, CancellationToken cancellationToken = default)
    {
        var mapUri = new Uri(http.BaseAddress
            ?? throw new InvalidOperationException("Map loading requires an HTTP base address."), mapPath);
        if (map.WorldMap == null || map.WorldMap.Any(column => column == null) || map.Objects == null)
            throw new InvalidDataException("Map grid and object index are required.");
        var objects = new Dictionary<int, MapObjectDefinition>();
        var pending = map.WorldMap.SelectMany(column => column).Distinct().ToArray();
        while (pending.Length > 0)
        {
            var definitions = await Task.WhenAll(pending.Select(async id =>
            {
                if (id < 0 || !map.Objects.TryGetValue(id, out var file) || string.IsNullOrWhiteSpace(file))
                    throw new InvalidDataException($"No object path for ID {id} in '{mapPath}'.");
                var definition = await http.GetFromJsonAsync<MapObjectDefinition>(new Uri(mapUri, file), Options, cancellationToken)
                    ?? throw new InvalidDataException($"Object file '{file}' is empty.");
                if (definition.Id != id)
                    throw new InvalidDataException($"Object '{file}' has ID {definition.Id}, expected {id}.");
                return definition;
            }));
            foreach (var definition in definitions) objects.Add(definition.Id, definition);
            pending = definitions.SelectMany(definition => definition switch
            {
                WallDefinition wall => new[] { wall.PassageId },
                DoorDefinition door => new[] { door.PassageId },
                SpriteDefinition sprite => new[] { sprite.PassageId },
                SpriteGroupDefinition group => new[] { group.PassageId }.Concat(
                    (group.Sprites ?? throw new InvalidDataException($"Sprite group {group.Id} has no sprite list."))
                    .Select(member => member?.SpriteId ?? throw new InvalidDataException($"Sprite group {group.Id} has a null member."))),
                _ => Enumerable.Empty<int>()
            }).Where(id => !objects.ContainsKey(id)).Distinct().ToArray();
        }
        return new ReadOnlyDictionary<int, MapObjectDefinition>(objects);
    }
}
