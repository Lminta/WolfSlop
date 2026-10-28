namespace WolfNET.Engine.World;

public interface IMap
{
    string GetImpactMaterial(int x, int y) => "hard";
    IReadOnlyList<AmbientEmitter> AmbientEmitters => [];
    bool Interact(System.Numerics.Vector2 position, System.Numerics.Vector2 direction, float radius);
    void UpdateDoors(float milliseconds, System.Numerics.Vector2 position, float radius);
    int[][] WorldMap { get; }
    WolfNET.Engine.Graphics.RenderMap RendererMap { get; }
    NavigationMap NavigationMap { get; }
    ulong Revision { get; }
    ActorStartDefinition Spawn { get; }
}

public readonly record struct AmbientEmitter(string Cue, System.Numerics.Vector2 Position);
