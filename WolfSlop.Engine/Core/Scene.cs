using System.Numerics;
using WolfSlop.Engine.Graphics;

namespace WolfSlop.Engine.Core;

public interface IScene : IDisposable
{
    void Update(float deltaMilliseconds);
    RenderFrame CaptureFrame();
}

public interface ISceneSystem
{
    void Update(Scene scene, float deltaMilliseconds);
}

public sealed class SceneCamera
{
    public Vector2 Position { get; set; }
    public Vector2 Direction { get; set; } = Vector2.UnitX;
    public Vector2 Plane { get; set; } = new(0, -.66f);
    public float Pitch { get; set; }
    public float Elevation { get; set; }
}

/// <summary>A reusable scene with application-supplied systems; no player or game mode is required.</summary>
public sealed class Scene(RenderMap map, SceneCamera? camera = null) : IScene
{
    private bool _disposed;
    public RenderMap Map { get; set; } = map;
    public SceneCamera Camera { get; } = camera ?? new();
    public ObjectCollection Objects { get; } = new();
    public IList<ISceneSystem> Systems { get; } = new List<ISceneSystem>();
    public IList<Sprite> DynamicSprites { get; } = new List<Sprite>();
    public IList<ScreenOverlay> Overlays { get; } = new List<ScreenOverlay>();
    public bool Paused { get; set; }
    public bool ShowMinimap { get; set; } = true;
    public bool CullSpritesOutsideView { get; set; }

    public void Update(float deltaMilliseconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!float.IsFinite(deltaMilliseconds) || deltaMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaMilliseconds));
        if (Paused) return;
        Objects.Update(deltaMilliseconds);
        foreach (var system in Systems) system.Update(this, deltaMilliseconds);
    }

    public RenderFrame CaptureFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new(Camera.Position, Camera.Direction, Camera.Plane, Map, Camera.Pitch, Camera.Elevation)
        {
            DynamicSprites = DynamicSprites as IReadOnlyList<Sprite> ?? DynamicSprites.ToArray(),
            Overlays = Overlays as IReadOnlyList<ScreenOverlay> ?? Overlays.ToArray(),
            ShowMinimap = ShowMinimap,
            CullSpritesOutsideView = CullSpritesOutsideView
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Objects.Dispose();
        foreach (var system in Systems.OfType<IDisposable>().Distinct()) system.Dispose();
        Systems.Clear(); DynamicSprites.Clear(); Overlays.Clear();
    }
}
