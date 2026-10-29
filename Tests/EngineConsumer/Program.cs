using System.Numerics;
using System.Text.Json;
using WolfSlop.Engine.Actors;
using WolfSlop.Engine.Audio;
using WolfSlop.Engine.Core;
using WolfSlop.Engine.Graphics;
using WolfSlop.Engine.Input;
using WolfSlop.Engine.Navigation;
using WolfSlop.Engine.World;

// This assembly references only Engine and receives no access to its internals.
int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
Check(!typeof(Scene).Assembly.GetReferencedAssemblies().Any(assembly => assembly.Name!.Contains("Examples")),
    "Engine depends on Examples.");
var engineAssembly = typeof(Scene).Assembly;
Check(!engineAssembly.GetReferencedAssemblies().Any(assembly =>
        assembly.Name!.StartsWith("SpawnDev", StringComparison.Ordinal) ||
        assembly.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
        assembly.Name.StartsWith("Microsoft.JSInterop", StringComparison.Ordinal) ||
        assembly.Name.StartsWith("WolfSlop.Engine.Sdl", StringComparison.Ordinal)),
    "The portable engine depends on a platform provider or browser framework.");
Check(!engineAssembly.GetCustomAttributesData().Any(attribute =>
        attribute.AttributeType.FullName == "System.Runtime.Versioning.SupportedOSPlatformAttribute"),
    "The portable engine is restricted to a particular platform.");

var definition = new MapDefinition
{
    WorldMap = Enumerable.Range(0, 8).Select(x => Enumerable.Range(0, 8)
        .Select(y => x == 0 || y == 0 || x == 7 || y == 7 ? 1 : 0).ToArray()).ToArray(),
    Metadata = new() { ["museum"] = JsonSerializer.SerializeToElement(new { title = "Custom application" }) }
};
var objects = new Dictionary<int, MapObjectDefinition>
{
    [0] = new PassageDefinition { Id = 0, Collides = false, RaycastTransparent = true,
        Surface = "ice", AmbientSound = "ventilation", ImpactMaterial = "crystal" },
    [1] = new WallDefinition { Id = 1, PassageId = 0, Collides = true, RaycastTransparent = false },
    [2] = new SpriteDefinition { Id = 2, PassageId = 0, Collides = false, RaycastTransparent = true,
        TextureLayer = 4, Tag = "exhibit", InteractionTarget = "inspect:exhibit-42" }
};
definition.WorldMap[4][3] = 2;
var map = new TileMap(definition, objects, 32, 32);
Check(!map.HasSpawn, "A tool must be able to load a map without an actor spawn.");
Check(map.NavigationMap.GetSurface(3, 3) == "ice", "Custom surface was lost.");
Check(map.GetImpactMaterial(3, 3) == "crystal", "Custom impact material was lost.");
Check(map.AmbientEmitters.All(emitter => emitter.Cue == "ventilation") && map.AmbientEmitters.Count > 0,
    "Custom ambient sound was lost.");
Check(MapInteraction.FindTarget(map, new(3.5f, 3.5f), Vector2.UnitX) == "inspect:exhibit-42",
    "Interaction targets must support application commands.");
Check(map.RendererMap.Sprites.Single().Tag == "exhibit", "Application sprite tags were lost.");
definition.Metadata.Clear();
map.SetCell(2, 2, 1);
Check(map.RendererMap.Metadata["museum"].GetProperty("title").GetString() == "Custom application",
    "External metadata mutation changed the loaded map.");
new ActorStartDefinition { Controller = "drone", X = 3, Y = 3, DirectionX = 1, DirectionY = 0,
    MoveSpeed = 0, RotSpeed = 0 }.Validate();
checks++;

var keys = new KeyboardState(new InputBindings(new Dictionary<string, string[]>
    { ["forward"] = ["ArrowUp"], ["inspect"] = ["KeyE", "Enter"] }));
keys.Press("ArrowUp"); keys.Press("KeyE");
Check(keys.KeyState.Forward && keys.IsHeld("inspect"), "Custom key bindings did not drive actions.");
Check(keys.ConsumePressed("inspect") && !keys.ConsumePressed("inspect"), "Custom action was not consumed once.");
keys.Reset();
Check(!keys.KeyState.Forward && !keys.IsHeld("inspect"), "Reset retained custom input state.");

using (var scene = new Scene(map.RendererMap))
{
    var system = new MotionSystem();
    scene.Systems.Add(system);
    for (var i = 0; i < 128; i++) scene.DynamicSprites.Add(new(new(3 + i * .01f, 4), 4, 0));
    for (var i = 0; i < 6; i++) scene.Overlays.Add(new(i, new(.5f), new(.2f), new(.5f), i * .1f));
    scene.Update(16);
    var frame = scene.CaptureFrame();
    Check(frame.DynamicSprites.Count == 128 && frame.Overlays.Count == 6, "Scene retained fixed sprite/overlay limits.");
    Check(frame.Position.X == 16, "Application scene systems were not updated.");
    scene.Paused = true; scene.Update(16);
    Check(scene.Camera.Position.X == 16, "Paused scene advanced its systems.");
    scene.Dispose(); scene.Dispose();
    Check(system.Disposals == 1, "Scene system was not disposed exactly once.");
}

var layers = new Dictionary<uint, string?> { [4] = "custom.png" };
var catalog = new TextureCatalog(layers);
layers.Clear();
Check(catalog.Layers.Count == 1 && catalog.LayerCount == 5, "Texture catalog did not retain an independent snapshot.");
new CarSettings { RoadSurfaces = ["ice", "stone"] }.Validate(); checks++;

var path = new PolylinePath([Vector2.Zero, new(5, 0), new(5, 5)]);
Check(path.Length == 10 && path.Point(-1) == Vector2.Zero && path.Point(12) == new Vector2(5),
    "Open paths must clamp their endpoints.");
Check(MathF.Abs(path.Project(new(6, 2)) - 7) < .00001f, "Path projection did not find the nearest segment.");
var loop = new PolylinePath([Vector2.Zero, new(5, 0), new(5, 5), new(0, 5)], loop: true);
Check(loop.Length == 20 && loop.Point(21) == Vector2.UnitX && loop.Project(Vector2.UnitX, 19) == 21,
    "Looping path queries did not preserve continuous progress.");
var gate = new DirectedGate(Vector2.Zero, Vector2.UnitX * 2, 1);
Check(gate.Crossed(new(-2, 0), new(2, 0)) == .5f &&
    gate.Crossed(new(2, 0), new(-2, 0)) == null && gate.Crossed(new(-2, 2), new(2, 2)) == null,
    "Directed triggers must reject backward and out-of-width crossings.");

var backend = new TestBackend();
using (var sound = new SpatialSoundSystem<string>(backend, maxVoices: 2))
{
    sound.Update(Vector2.Zero, Vector2.UnitX, [new(1, "vent", new(2, 0)), new(2, "water", new(0, -2))]);
    Check(backend.Voices.Count == 2 && backend.Voices[1].Pan > .99f, "Custom cues did not produce positioned voices.");
    Check(!sound.Play(new(3, "button", Vector2.Zero)), "Voice limit was exceeded.");
    sound.Update(Vector2.Zero, Vector2.UnitX, [new(2, "water", new(0, -2))]);
    Check(backend.Voices[0].Disposals == 1, "Removed emitter retained its voice.");
    Check(sound.Play(new(3, "button", Vector2.Zero)), "One-shot did not use released voice capacity.");
    backend.Voices[^1].Finished = true;
    sound.Update(Vector2.Zero, Vector2.UnitX, [], active: false);
    Check(backend.Paused && backend.Voices[1].Disposals == 0, "Pause destroyed continuing voices.");
    sound.Update(Vector2.Zero, Vector2.UnitX, []);
    Check(backend.Voices.All(voice => voice.Disposals == 1), "Resume did not clean finished shots and absent loops.");
}
Check(backend.Disposals == 1, "Spatial sound system did not own its backend.");
checks += UiChecks.Run();
Console.WriteLine($"PASS: {checks} independent engine consumer assertions (no game reference, custom maps/input/audio, scenes, and lifetime).");

sealed class MotionSystem : ISceneSystem, IDisposable
{
    public int Disposals;
    public void Update(Scene scene, float milliseconds) => scene.Camera.Position += new Vector2(milliseconds, 0);
    public void Dispose() => Disposals++;
}
sealed class TestBackend : ISfxBackend<string>
{
    public List<TestVoice> Voices = [];
    public bool Paused;
    public int Disposals;
    public ISfxVoice Play(string cue, bool loop, float phase = 0, int emitterId = 0, int? variantIndex = null)
    { var voice = new TestVoice(); Voices.Add(voice); return voice; }
    public void SetPaused(bool paused) => Paused = paused;
    public void Dispose() => Disposals++;
}
sealed class TestVoice : ISfxVoice
{
    public bool Finished { get; set; }
    public float Pan;
    public int Disposals;
    public void SetMix(float volume, float pan, float pitch) => Pan = pan;
    public void Dispose() => Disposals++;
}
