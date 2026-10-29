using WolfSlop.Engine.Graphics;

namespace WolfSlop.Engine.Browser.Graphics;

public sealed record ShaderSources(string Raycast, string Walls, string Sprites, string Overlays)
{
    public static async Task<ShaderSources> LoadAsync(HttpClient http, CancellationToken cancellationToken = default)
    {
        var raycast = http.GetStringAsync("_content/WolfSlop.Engine.Browser/shaders/raycast.wgsl", cancellationToken);
        var walls = http.GetStringAsync("_content/WolfSlop.Engine.Browser/shaders/walls.wgsl", cancellationToken);
        var sprites = http.GetStringAsync("_content/WolfSlop.Engine.Browser/shaders/sprites.wgsl", cancellationToken);
        var firstPerson = http.GetStringAsync("_content/WolfSlop.Engine.Browser/shaders/overlays.wgsl", cancellationToken);
        await Task.WhenAll(raycast, walls, sprites, firstPerson);
        return new(await raycast, await walls, await sprites, await firstPerson);
    }
}
