# Engine checks

```sh
dotnet run --project Tests/EngineTests/WolfSlop.EngineTests.csproj
dotnet run --project Tests/EngineConsumer/WolfSlop.EngineConsumer.csproj
```

The portable suite covers maps, doors, walking/driving, collisions, cameras, input/timing, animation, audio policy, and ownership. The independent consumer checks custom scenes, public APIs, UI layout/transforms/clipping/input/focus/modal behavior, and the platform-independent assembly boundary.

Native GPU/device integration belongs to the consuming application's smoke tests. The shader compiler under `Tools/ShaderCompiler` rebuilds precompiled SPIR-V and DXIL from HLSL.
