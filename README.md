# WolfSlop

Portable .NET 10 game engine and Blazor WebAssembly browser bindings, maintained
as an independent Git repository. No game project or game assets are required to
build or test this repository.

- `WolfNET.Engine/`: maps, scenes/components, input, physics, cameras, animation,
  audio policy, rendering contracts, and retained UI.
- `WolfNET.Engine.Browser/`: WebGPU, Canvas UI, browser input, frame scheduling,
  Web Audio, JavaScript, and shaders.
- `Tests/EngineTests/`: portable implementation checks and native sprite-order
  reference generation using procedural fixtures.
- `Tests/EngineConsumer/`: independent public API and UI checks, referencing only
  the portable engine without access to its internals.
- `Tests/*.test.mjs`: browser-provider unit checks using Node's test runner.

Run from this repository's root with the .NET 10 SDK and Node 22 or newer:

```powershell
dotnet build WolfSlop.slnx
dotnet run --project Tests/EngineTests/WolfNET.EngineTests.csproj
dotnet run --project Tests/EngineConsumer/WolfNET.EngineConsumer.csproj
node --test Tests/sprite-sort.test.mjs Tests/texture-upload.test.mjs Tests/sfx-mixer.test.mjs Tests/ui.test.mjs
```

Run the C# engine checks before the sprite-sort JavaScript checks: they generate
the native reference fixtures in `artifacts/performance`. Neither those fixtures
nor any test requires Pine Loop, Examples, Race Imperialis, a browser, or a GPU.

See the [engine guide](WolfNET.Engine/README.md),
[browser bindings](WolfNET.Engine.Browser/README.md),
[UI guide](WolfNET.Engine/UI/README.md), and
[platform architecture](WolfNET.Engine/PLATFORMS.md) for integration details.
Applications supply their own assets and game rules. Provider assets keep their
`_content/WolfNET.Engine.Browser/` URLs regardless of the checkout location.
