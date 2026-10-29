# WolfNET.Engine.Browser

The current browser provider for [WolfNET.Engine](../WolfSlop.Engine/README.md).
This .NET 10 Razor class library targets Blazor WebAssembly and owns the browser
dependencies: SpawnDev.BlazorJS, JavaScript interop, DOM, Canvas 2D, WebGPU and Web Audio.

Reference this project/package alongside Engine from a Blazor WebAssembly host.
Register `AddBlazorJSRuntime()` and start with `BlazorJSRunAsync()`. See
[`Program.cs`](https://github.com/Lminta/WolfNET/blob/dev/WolfNET.Examples/Program.cs) and
[`Viewer.razor`](https://github.com/Lminta/WolfNET/blob/dev/WolfNET.Examples/Pages/Viewer.razor) for complete composition.

| Namespace | Provider implementation |
| --- | --- |
| `WolfNET.Engine.Browser.Graphics` | `WebGpuRenderer`, `ShaderSources`, GPU textures and accelerated sprite selection |
| `WolfNET.Engine.Browser.UI` | `BrowserUiHost`: Canvas UI, font/image loading, browser text editing and accessibility |
| `WolfNET.Engine.Browser.Input` | `BrowserInput`, `KeyTracker`, `MouseTracker`: events and pointer capture |
| `WolfNET.Engine.Browser.Audio` | `BrowserAudioBackend`, `BrowserSfxBackend<TCue>`: Web Audio playback |
| `WolfNET.Engine.Browser.Core` | `BrowserFrameLoop`: animation-frame scheduling implementing `IFrameLoop` |

JavaScript and WGSL shaders ship as static web assets under
`_content/WolfNET.Engine.Browser/`. Applications supply maps, artwork, clips and settings.
Use a secure browser context (`https` or `localhost`) and a WebGPU-capable device
for world rendering. `BrowserUiHost` requires in-process WebAssembly interop;
the `/ui` gallery does not require WebGPU.

`RendererSettings.ColumnInterlaceFactor` selects full rendering (`1`, the default)
or alternating vertical columns (`2` or `3`). It can change between frames.
Interlaced modes retain world colors in an offscreen texture and render the current
phase into a compact half-width or third-width target. Rays and sprite projections
use the original pixel centers; presentation combines the new columns with history.
World command encoding runs in one browser call for all modes, avoiding per-command
WASM interop overhead. The first frame
after preparing a scene or changing modes is complete. Screen overlays and the
minimap render after presentation every frame. Fast motion can show temporal trails;
the actual FPS gain depends on the GPU, CPU workload, target FPS, and display refresh rate.

Hosts can call `WebGpuRenderer.ApplySettings(settings, canvas)` between frames to
resize ray buffers and replace filtering samplers without reloading scene textures.
The host also updates the scene camera and calls `BrowserFrameLoop.SetTargetFPS`
when changing the frame cap. Interlaced history is refreshed after settings change.

The UI host sends changed display-list entries and preserves browser-side vector
geometry between frames. View transforms and text updates reuse immutable drawings
and cached text layouts; document changes and resize still refresh the full view.
The provider retains prepared sprite records and atlas widths/geometry in JavaScript,
resolves scenery views only in nearby spatial buckets, and uploads sorted GPU records
without reading them back through C#. Camera/actor inputs travel together. UI viewport
sizes follow resize notifications; wheel rotations and map drawings reuse layout.
Transform, text, and drawing changes use compact UI deltas. Glyph bitmaps are cached
until their layout, appearance, font metrics, or output scale changes; moving a
decorative image does not rebuild the accessibility tree. Music clocks are sampled
together and sound-effect parameters cross WASM as one byte batch. Hosts can use
`BrowserAudioBackend.CreateAsync` and `BrowserInput.CreateAsync` to enable batched
music state reads and a single browser call for pointer-capture checks.
World-only frames select sprites, upload camera/actor data and submit WebGPU commands
in one WASM call. Buffer growth returns the prepared selection to the managed owner
before upload; resizing and filtering changes clear cached submission bindings.
Frames with screen overlays continue through the screen-layer encoder.

The host creates the canvas, initializes the provider and owns its lifetime.
Dispose input subscriptions, frame loops, UI hosts, renderers and audio owners
when leaving a screen. Engine receives only maps, frames, input state, elapsed
milliseconds and provider interfaces; it never receives browser handles.

Android and native Windows support will use separate providers. This package is
not a native provider. See the [platform architecture guide](../WolfSlop.Engine/PLATFORMS.md)
and [UI guide](../WolfSlop.Engine/UI/README.md) before adding platform-specific code.
