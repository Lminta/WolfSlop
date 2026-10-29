using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

using WolfNET.Engine.Input;

namespace WolfNET.Engine.Browser.Input;

/// <summary>Owns and connects the input subscriptions for one scene.</summary>
public sealed class BrowserInput : IDisposable
{
    private readonly Window _window;
    private readonly JSObject? _captureModule;
    public KeyTracker Keyboard { get; }
    public MouseTracker Mouse { get; }

    public BrowserInput(HTMLCanvasElement canvas, BlazorJSRuntime js, InputBindings? bindings = null) : this(canvas, js, bindings, null) { }

    private BrowserInput(HTMLCanvasElement canvas, BlazorJSRuntime js, InputBindings? bindings, JSObject? captureModule)
    {
        _captureModule = captureModule;
        _window = js.Get<Window>("window");
        Mouse = new MouseTracker(canvas, js, captureModule == null ? null : () => captureModule.JSRef!.Call<bool>("isCaptured", canvas));
        try
        {
            Keyboard = new KeyTracker(js, () => Mouse.IsLocked, bindings);
            Keyboard.EscapePressed += Mouse.Release;
            Mouse.PointerLockReleased += Keyboard.Reset;
            _window.OnBlur += OnBlur;
        }
        catch
        {
            Mouse.Dispose();
            _window.Dispose();
            throw;
        }
    }

    public static async Task<BrowserInput> CreateAsync(HTMLCanvasElement canvas, BlazorJSRuntime js, InputBindings? bindings = null)
    {
        var module = await js.Import<JSObject>("./_content/WolfNET.Engine.Browser/input-state.js");
        try { return new(canvas, js, bindings, module); }
        catch { module.Dispose(); throw; }
    }

    private void OnBlur(Event e) => Mouse.Release();

    public void Dispose()
    {
        _window.OnBlur -= OnBlur;
        Keyboard.EscapePressed -= Mouse.Release;
        Mouse.PointerLockReleased -= Keyboard.Reset;
        Keyboard.Dispose();
        Mouse.Dispose();
        _window.Dispose();
        _captureModule?.Dispose();
    }
}
