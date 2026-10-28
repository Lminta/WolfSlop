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

The UI host sends changed display-list entries and preserves browser-side vector
geometry between frames. View transforms and text updates reuse immutable drawings
and cached text layouts; document changes and resize still refresh the full view.

The host creates the canvas, initializes the provider and owns its lifetime.
Dispose input subscriptions, frame loops, UI hosts, renderers and audio owners
when leaving a screen. Engine receives only maps, frames, input state, elapsed
milliseconds and provider interfaces; it never receives browser handles.

Android and native Windows support will use separate providers. This package is
not a native provider. See the [platform architecture guide](../WolfSlop.Engine/PLATFORMS.md)
and [UI guide](../WolfSlop.Engine/UI/README.md) before adding platform-specific code.
