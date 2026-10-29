# WolfSlop.Engine

Portable .NET 10 worlds, scenes, physics, input state, audio policy, and retained UI. The core has no NuGet dependencies and never receives a platform window or GPU handle.

Reference `WolfSlop.Engine.csproj` from a game or tool. Interactive native hosts additionally reference `WolfSlop.Engine.Sdl.csproj` and supply SDL3, SDL_mixer, and SkiaSharp native runtime packages for their platform.

Load assets through `IAssetSource`. `DirectoryAssetSource` confines file access to a content directory; packaged hosts can implement the interface themselves. `ReadJsonAsync<T>` supports map/settings definitions and polymorphic object metadata. `TileMap.LoadAsync(assets, path, maxWidth, maxHeight)` resolves object files relative to their map.

Construct a `Scene` or application-owned `IScene`, update it in milliseconds, and capture a `RenderFrame`. Scene, physics, cameras, map compilation, navigation, animation, and audio policy remain independent of the rendering provider. Games own their rules and assets.

`IKeyboardInput`, `IMouseInput`, and `KeyboardState` connect controls to actors. `ISceneRenderer` consumes maps and camera snapshots. `IAudioBackend`/`IAudioStream` connect native playback to `MusicPlayer`; `ISfxBackend<TCue>` connects application sound cues to portable spatial mixing policy. `IUiRenderer` and `IUiTextMeasurer` present UI documents.

See [platform architecture](PLATFORMS.md) for ownership and native integration and [UI](UI/README.md) for layout and controls. Independent consumer checks verify that the core remains usable without a native window or graphics provider.
