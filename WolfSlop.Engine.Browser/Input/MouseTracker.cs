using System.Numerics;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

using WolfSlop.Engine.Input;

namespace WolfSlop.Engine.Browser.Input;

public sealed class MouseTracker : IMouseInput, IDisposable
{
    private readonly HTMLCanvasElement _canvas;
    private readonly Document _document;
    private Vector2 _movement;
    private bool _disposed;
    private bool _wasLocked;
    private bool _requestPending;
    private bool _awaitingPromise;
    private bool _releasePending;
    private int _requestId;
    private bool? _frameCapture;

    public bool IsLocked => !_disposed && !_releasePending && (_frameCapture ?? HasPointerLock());
    public string? CaptureError { get; private set; }
    public event Action? PointerLockReleased;
    public event Action? CaptureStateChanged;

    /// <summary>Samples capture once for a synchronous game frame. Outside this scope,
    /// reads still query the DOM so an Escape release is observed before its event arrives.</summary>
    public CaptureSample SampleForFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var previous = _frameCapture;
        _frameCapture = HasPointerLock();
        return new(this, previous);
    }

    public readonly ref struct CaptureSample
    {
        private readonly MouseTracker _owner;
        private readonly bool? _previous;
        internal CaptureSample(MouseTracker owner, bool? previous) => (_owner, _previous) = (owner, previous);
        public void Dispose() => _owner._frameCapture = _previous;
    }

    private bool HasPointerLock()
    {
        using var lockedElement = _document.PointerLockElement;
        return lockedElement != null && lockedElement.IsSameNode(_canvas);
    }

    public MouseTracker(HTMLCanvasElement canvas, BlazorJSRuntime js)
    {
        _canvas = canvas;
        _document = js.Get<Document>("document");
        _canvas.OnClick += OnCanvasClick;
        _document.OnMouseMove += OnMouseMove;
        _document.OnPointerLockChange += OnPointerLockChange;
        _document.OnPointerLockError += OnPointerLockError;
    }

    private async void OnCanvasClick(MouseEvent e)
    {
        if (_disposed || _requestPending || HasPointerLock()) return;
        // An exit is asynchronous too. Never overlap it with a new request.
        _releasePending = false;
        _requestPending = true;
        var requestId = ++_requestId;
        CaptureError = null;
        // Retain DOM references until the asynchronous request settles, even on map changes.
        using var document = _document.JSRefCopy<Document>();
        using var canvas = _canvas.JSRefCopy<HTMLCanvasElement>();
        try
        {
            _canvas.Focus();
            // Invoke before the first await to preserve the click's user activation.
            // Older browsers return undefined and report the result through document events.
            using var promise = _canvas.JSRef!.Call<Promise?>("requestPointerLock");
            _awaitingPromise = promise != null;
            if (promise != null) await promise.ThenAsync();
            if (_disposed)
            {
                using var locked = document.PointerLockElement;
                if (locked != null && locked.IsSameNode(canvas)) document.ExitPointerLock();
                return;
            }
            if (promise != null && requestId == _requestId)
            {
                _requestPending = false;
                SynchronizeCapture();
            }
        }
        catch (Exception ex)
        {
            if (!_disposed && requestId == _requestId) CaptureFailed(ex.Message);
        }
        finally
        {
            if (requestId == _requestId) _awaitingPromise = false;
        }
    }

    private void OnMouseMove(MouseEvent e)
    {
        if (IsLocked)
            _movement += new Vector2((float)e.MovementX, (float)e.MovementY);
    }

    private void OnPointerLockChange(Event e)
    {
        if (_disposed) return;
        SynchronizeCapture();
    }

    private void SynchronizeCapture()
    {
        var locked = HasPointerLock();
        if (_frameCapture != null) _frameCapture = locked;
        var wasLocked = _wasLocked;
        _wasLocked = locked;
        if (locked || wasLocked) _requestPending = false;
        if (!locked) _releasePending = false;
        _movement = Vector2.Zero;
        if (locked) CaptureError = null;
        if (wasLocked && !locked) PointerLockReleased?.Invoke();
        CaptureStateChanged?.Invoke();
    }

    private void OnPointerLockError(Event e)
    {
        // Promise failures are handled by the request that owns them. A delayed error
        // must never disable a lock that the browser has already granted.
        if (!_disposed && !_awaitingPromise) CaptureFailed();
    }

    private void CaptureFailed(string? reason = null)
    {
        if (HasPointerLock())
        {
            SynchronizeCapture();
            return;
        }
        _requestPending = false;
        _releasePending = false;
        _wasLocked = false;
        if (_frameCapture != null) _frameCapture = false;
        _movement = Vector2.Zero;
        PointerLockReleased?.Invoke();
        CaptureError = "Mouse capture was blocked. If you just pressed Esc, wait a moment, then click the game again.";
        Console.Error.WriteLine($"[INPUT] Mouse capture rejected: {reason ?? "pointerlockerror"}");
        CaptureStateChanged?.Invoke();
    }

    public Vector2 ConsumeMovement()
    {
        // Escape can release the browser lock before pointerlockchange is delivered.
        // Drop queued deltas during that gap, including cursor restoration movement.
        var movement = IsLocked ? _movement : Vector2.Zero;
        _movement = Vector2.Zero;
        return movement;
    }

    public void Release()
    {
        var wasLocked = _wasLocked || HasPointerLock();
        _releasePending = HasPointerLock();
        _wasLocked = false;
        if (_frameCapture != null) _frameCapture = false;
        _movement = Vector2.Zero;
        if (wasLocked) PointerLockReleased?.Invoke();
        if (_releasePending)
            _document.ExitPointerLock();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Release();
        PointerLockReleased = null;
        CaptureStateChanged = null;
        _canvas.OnClick -= OnCanvasClick;
        _document.OnMouseMove -= OnMouseMove;
        _document.OnPointerLockChange -= OnPointerLockChange;
        _document.OnPointerLockError -= OnPointerLockError;
        _document.Dispose();
    }
}
