# WolfSlop

A portable .NET 10 tile-world engine with an SDL3 / SDL GPU native provider.

`WolfSlop.Engine` contains maps, actors, physics, scenes, audio policy, input state, retained UI, and provider contracts. It has no platform package dependencies.

`WolfSlop.Engine.Sdl` implements native GPU raycasting, textured walls/floors/ceilings, skies and sprites; SDL input; SDL_mixer music/effects; and SkiaSharp UI drawing. Desktop and Android hosts supply the appropriate native runtime packages.

Build with `dotnet build WolfSlop.slnx`. See [platform architecture](WolfSlop.Engine/PLATFORMS.md), [core guide](WolfSlop.Engine/README.md), and [UI guide](WolfSlop.Engine/UI/README.md).

Run the portable checks:

```sh
dotnet run --project Tests/EngineTests/WolfSlop.EngineTests.csproj
dotnet run --project Tests/EngineConsumer/WolfSlop.EngineConsumer.csproj
```

Compile native shaders after editing HLSL:

```sh
dotnet run --project Tools/ShaderCompiler/ShaderCompiler.csproj
```
