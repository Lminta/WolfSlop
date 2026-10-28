<h1 align="center">WolfSlop</h1>

<p align="center">
  <img src="docs/images/wolf.png" alt="A snow-covered gray wolf" width="320">
</p>

<p align="center">
  <strong>A .NET raycasting engine with WebGPU rendering.</strong><br>
  Tile worlds, scenes, physics, UI, and audio — with a portable core and browser bindings.
</p>

<p align="center">
  <a href="WolfSlop.Engine/README.md">Engine guide</a> &middot;
  <a href="WolfSlop.Engine.Browser/README.md">Browser bindings</a> &middot;
  <a href="WolfSlop.Engine/UI/README.md">UI guide</a> &middot;
  <a href="#getting-started">Getting started</a>
</p>

## What you can build

WolfSlop provides the systems for exploring tile worlds and driving through them.
Your application supplies the maps, artwork, sounds, and game rules.

- **Worlds and scenes:** tile maps, editable geometry, doors, sprites, animation,
  objects, and components.
- **Movement and cameras:** walking, jumping, crouching, vehicle handling,
  collision, and first- and third-person views.
- **Screen UI:** retained layout, text, images, vector drawing, controls,
  keyboard focus, and modal input.
- **Sound:** music queues, crossfades, spatial audio, and provider interfaces.
- **Browser rendering:** WebGPU, Canvas UI, browser input, frame scheduling,
  and Web Audio through Blazor WebAssembly.

## Two projects, one engine

| Project | Responsibility |
| --- | --- |
| [**WolfSlop.Engine**](WolfSlop.Engine/README.md) | Portable .NET worlds, simulation, UI, audio policy, and rendering contracts. |
| [**WolfSlop.Engine.Browser**](WolfSlop.Engine.Browser/README.md) | Browser rendering, input, scheduling, audio playback, JavaScript, and shaders. |

Use the core independently in a .NET application or tool. Add the browser
provider when you want to render and play in a WebGPU-capable browser.
Game-specific logic and assets stay in the consuming application.

## Getting started

Build with the **.NET 10 SDK**:

```sh
git clone https://github.com/Lminta/WolfSlop.git
cd WolfSlop
dotnet build WolfSlop.slnx
```

Reference [the core project](WolfSlop.Engine/WolfNET.Engine.csproj) from your
application. For Blazor WebAssembly, also reference
[the browser provider](WolfSlop.Engine.Browser/WolfNET.Engine.Browser.csproj).
The [engine guide](WolfSlop.Engine/README.md#reference-and-initialize) walks
through loading a map, creating a scene, and starting a render loop.

Browser rendering requires **HTTPS or localhost** and **WebGPU support**.
The public APIs use `WolfNET.Engine.*` and `WolfNET.Engine.Browser.*` namespaces.
Provider assets are served automatically under `_content/WolfNET.Engine.Browser/`.

## Documentation

| Guide | Covers |
| --- | --- |
| [Engine](WolfSlop.Engine/README.md) | Initialization, extension points, maps, timing, and ownership. |
| [Browser bindings](WolfSlop.Engine.Browser/README.md) | Blazor hosting, WebGPU, input, UI, and audio. |
| [UI](WolfSlop.Engine/UI/README.md) | Layout, drawing, controls, focus, and input. |
| [Platform architecture](WolfSlop.Engine/PLATFORMS.md) | Provider boundaries and plans for additional platforms. |
| [Tests](Tests/README.md) | Regression checks, public API coverage, and procedural fixtures. |

## Run the checks

The tests run independently of any game, without game assets, a browser, or a GPU.
Use **Node 22 or newer** for the JavaScript checks.

<details>
<summary>Engine, public API, and browser-provider checks</summary>

Run from the repository root, in this order:

```sh
dotnet run --project Tests/EngineTests/WolfNET.EngineTests.csproj
dotnet run --project Tests/EngineConsumer/WolfNET.EngineConsumer.csproj
node --test Tests/sprite-sort.test.mjs Tests/texture-upload.test.mjs Tests/sfx-mixer.test.mjs Tests/ui.test.mjs
```

The engine checks generate the native sprite-order reference fixtures in
`artifacts/performance` before the JavaScript suite compares its results.
The independent consumer references only the portable core and checks its public
API without access to engine internals.

</details>
