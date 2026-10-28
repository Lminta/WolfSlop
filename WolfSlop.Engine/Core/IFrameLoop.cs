namespace WolfNET.Engine.Core;

/// <summary>Host-owned frame scheduling. Deltas are milliseconds; callbacks run on the host's update thread.</summary>
public interface IFrameLoop : IDisposable
{
    bool Running { get; }

    /// <summary>Replaces any previous callback and starts with reset timing.</summary>
    void Start(Action<float> frame, Action<Exception>? failed = null);

    /// <summary>Prevents further callbacks, including callbacks queued before a restart.</summary>
    void Stop();

    /// <summary>Excludes loading or suspension time from the next simulation delta.</summary>
    void ResetTiming();
}
