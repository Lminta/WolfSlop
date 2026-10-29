namespace WolfSlop.Engine.Core;

/// <summary>Limits frame frequency from host timestamps without blocking or accumulating catch-up frames.</summary>
public sealed class FrameTiming
{
    private readonly double _interval;
    private double _lastFrame;
    private double _nextFrame;

    public FrameTiming(int targetFramesPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetFramesPerSecond);
        _interval = 1000.0 / targetFramesPerSecond;
    }

    public void Reset(double now = 0) => _lastFrame = _nextFrame = now;

    public bool TryAdvance(double now, out float deltaMilliseconds)
    {
        deltaMilliseconds = 0;
        if (now + 0.5 < _nextFrame) return false;
        deltaMilliseconds = (float)Math.Clamp(now - _lastFrame, 0, 100);
        _lastFrame = now;
        _nextFrame += Math.Max(1, Math.Floor((now + 0.5 - _nextFrame) / _interval) + 1) * _interval;
        return true;
    }
}
