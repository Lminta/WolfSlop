# WolfNET.Engine

A platform-independent .NET 10 class library for tile maps, scenes and components,
input state, collision, optional walking and vehicle controllers, cameras, sprite
animation, retained UI, audio policy and rendering contracts.

The library has no dependency on `WolfNET.Examples`, its assets, race rules,
opponents, or UI. It has no NuGet dependencies, browser interop, Blazor references,
static web assets, or platform restriction. See [platform architecture](PLATFORMS.md)
for provider boundaries and the plan for future Android and native Windows support.

## Reference and initialize

Reference `WolfNET.Engine.csproj` to use the portable engine in a .NET host or tool.
Browser applications also reference
[`WolfNET.Engine.Browser.csproj`](../WolfSlop.Engine.Browser/WolfNET.Engine.Browser.csproj).
That provider supplies `WebGpuRenderer`, `BrowserUiHost`, `BrowserInput`, browser
audio and `BrowserFrameLoop`, under `WolfNET.Engine.Browser.*` namespaces.
The Blazor WebAssembly host registers
`AddBlazorJSRuntime()` and starts with `BlazorJSRunAsync()`, as shown in
[`WolfNET.Examples/Program.cs`](https://github.com/Lminta/WolfSlop.Examples/blob/dev/WolfSlop.Examples/Program.cs).

Provider JavaScript and shaders are Razor static web assets, served automatically
under `_content/WolfNET.Engine.Browser/`. Do not copy them into the application. Artwork,
map/object JSON, audio clips, settings, and UI belong to the host application.
Use a secure browser context (`https` or `localhost`) with WebGPU support.

```csharp
using WolfNET.Engine.Core;
using WolfNET.Engine.Graphics;
using WolfNET.Engine.World;
using WolfNET.Engine.Browser.Core;
using WolfNET.Engine.Browser.Graphics;

var settings = new RendererSettings(960, 540);
var map = await TileMap.LoadAsync(http, "maps/room.json",
    settings.MaxMapWidth, settings.MaxMapHeight, cancellationToken);
using var scene = new Scene(map.RendererMap);
scene.Camera.Position = new(4.5f, 4.5f);
scene.Camera.Direction = new(1, 0);
scene.Camera.Plane = new(0, -settings.CameraPlaneScale);

using var renderer = new WebGpuRenderer(settings, js);
await renderer.InitializeAsync(canvas,
    await ShaderSources.LoadAsync(http, cancellationToken), cancellationToken);
await renderer.PrepareSceneAsync(scene.Map, cancellationToken);
using var loop = new BrowserFrameLoop(js, settings.TargetFPS);
loop.Start(milliseconds =>
{
    scene.Update(milliseconds);
    renderer.Render(scene.CaptureFrame());
});
// Keep these owners alive until the host component is disposed.
```

[`Viewer.razor`](https://github.com/Lminta/WolfSlop.Examples/blob/dev/WolfSlop.Examples/Pages/Viewer.razor) is a complete procedural
viewer using `Scene` and an `ISceneSystem`, with twelve moving sprites and no actor,
controls, race state, or audio configuration. The exploration/racing example
implements `IScene` separately, showing that applications can own scene assembly.

## Extension points

| System | Application supplies |
| --- | --- |
| Scene | `IScene` or `Scene` with `ISceneSystem` instances and `ObjectBase` components |
| Frame scheduling | `IFrameLoop`; the browser provider implements it with `BrowserFrameLoop` |
| Rendering | A `RenderFrame` with camera, map, arbitrary `DynamicSprites`, and ordered `ScreenOverlay` layers |
| Scene resources | Optional extra `TextureCatalog` entries and overlay image paths through `SceneResources` |
| UI | `UiDocument`, layout/styles, text/images/vector widgets and controls; `IUiRenderer` and `IUiTextMeasurer` implementations |
| Input | `InputBindings` mapping action names to key codes, or `IKeyboardInput`/`IMouseInput` implementations |
| Physics | `INavigationMap`, circle/box colliders, or optional `WalkingController`/`CarController` |
| Paths and triggers | `PolylinePath` for indexed open/looping paths and `DirectedGate` for directed crossing sensors |
| Vehicle terrain | Surface identifiers in `CarSettings.RoadSurfaces` and primary/off-road handling profiles |
| Map behavior | Arbitrary JSON `Metadata`, sprite `Tag`, and `InteractionTarget` commands |
| Audio | `ISfxBackend<TCue>`, `SpatialSoundSystem<TCue>`, and application-defined cue types and clip dictionaries |
| Music | `MusicPlayer`, `MusicDefinition`, `MusicSettings`, and `IAudioBackend` |

The renderer never interprets map metadata, sprite tags, controller names, or
interaction targets. For example, an interaction target can be `inspect:42`, an
inventory action, or a scene key. `MapInteraction.FindTarget` traces a configurable
distance with occlusion; the application decides what the returned value means.
Racing metadata parsing and validation live entirely in the example.

`ScreenOverlay` coordinates are normalized to the viewport. `Position` anchors
the layer's local `Pivot`, `Size` controls its dimensions, `Rotation` is in radians,
and `UV` selects a texture rectangle. Overlay texture indices refer to
`SceneResources.OverlayTextures`. Layers are composited in list order without
world depth or fog. This remains a low-level graphics API. The examples' hands,
cockpit, minimap, timing, countdown and results use the retained UI system described
in the [UI guide](UI/README.md). `/ui` is an interactive control gallery.

## Maps and assets

Maps use `worldMap[x][y]` and an object registry whose JSON paths are relative to
the map file. Passages, walls, doors, sprites, and sprite groups preserve separate
rendering, collision, jump/crouch, material, and animation properties. One cell is
one world unit; outer boundaries must block movement and rays. Texture paths are
relative to the host's base URL. There are at most 256 world texture layers.

`spawn` is optional. Applications that use the bundled controllers can declare an
`ActorStartDefinition`; custom controller names are accepted. Scenes and viewers
can omit spawn data. Surface, impact-material, and ambient-sound identifiers are
application-defined strings. Unknown top-level JSON fields are retained in
`MapDefinition.Metadata` and the compiled `RenderMap.Metadata`.

`TileMap.SetCell` and `ReplaceMap` publish only successful compilations and keep
the navigation object stable. Doors update through `Interact` and `UpdateDoors`.
If editing changes the texture catalog, serialize a new `PrepareSceneAsync`
before rendering again; include the current extra textures and overlays.

## Timing and ownership

Simulation update durations are milliseconds; controller speeds use world units
and radians per second. The browser provider's `BrowserFrameLoop` follows animation timestamps and
invalidates stale callbacks on stop/restart. Vehicle simulation uses fixed 120 Hz
steps. Applications own pause rules, map transitions, and renderer preparation.

Scenes own registered objects, components, and disposable scene systems.
`BrowserInput` owns browser input subscriptions; capture loss clears input.
`WebGpuRenderer` owns GPU resources and accepts one scene preparation at a time.
`MusicPlayer` and `SpatialSoundSystem` own their audio backends. Dispose these
owners when switching scenes or leaving the host component, and pass cancellation
tokens to asynchronous loading/preparation.

## Validation and packaging

```powershell
dotnet run --project Tests/EngineConsumer/WolfNET.EngineConsumer.csproj
dotnet pack WolfSlop.Engine/WolfNET.Engine.csproj -o artifacts/packages
dotnet pack WolfSlop.Engine.Browser/WolfNET.Engine.Browser.csproj -o artifacts/packages
```

The independent consumer references only the engine and exercises its public API.
The Engine package contains the portable library and documentation. The separate
Browser package depends on Engine and includes browser implementations and static
web assets. Native applications can reference Engine alone.
