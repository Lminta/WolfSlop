using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

using WolfSlop.Engine.Core;

namespace WolfSlop.Engine.Browser.Core;

/// <summary>Display-clock frame pacing with cancellation by generation. Owns no scene or renderer.</summary>
public sealed class BrowserFrameLoop : IFrameLoop
{
    private readonly Window _window;
    private readonly FrameTiming _timing;
    private double _origin = double.NaN;
    private int _generation;
    private bool _disposed;
    public bool Running { get; private set; }

    public BrowserFrameLoop(BlazorJSRuntime js, int targetFramesPerSecond)
    {
        _timing = new(targetFramesPerSecond);
        _window = js.Get<Window>("window");
    }

    public void Start(Action<float> frame, Action<Exception>? failed = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        Stop();
        Running = true;
        var generation = _generation;
        void Tick(double timestamp)
        {
            if (!Running || _disposed || generation != _generation) return;
            try
            {
                if (double.IsNaN(_origin)) _origin = timestamp;
                if (_timing.TryAdvance(timestamp - _origin, out var milliseconds)) frame(milliseconds);
                if (Running && !_disposed && generation == _generation) _window.RequestAnimationFrame(Tick);
            }
            catch (Exception exception)
            {
                Stop();
                if (failed != null) failed(exception);
                else Console.Error.WriteLine($"[ENGINE] Frame failed: {exception}");
            }
        }
        _window.RequestAnimationFrame(Tick);
    }

    public void ResetTiming()
    {
        _origin = double.NaN;
        _timing.Reset();
    }

    public void Stop()
    {
        Running = false;
        _generation++;
        ResetTiming();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        _window.Dispose();
    }
}
