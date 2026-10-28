using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

using WolfNET.Engine.Input;

namespace WolfNET.Engine.Browser.Input;

/// <summary>Owns and connects the input subscriptions for one scene.</summary>
public sealed class BrowserInput : IDisposable
{
    private readonly Window _window;
    public KeyTracker Keyboard { get; }
    public MouseTracker Mouse { get; }

    public BrowserInput(HTMLCanvasElement canvas, BlazorJSRuntime js, InputBindings? bindings = null)
    {
        _window = js.Get<Window>("window");
        Mouse = new MouseTracker(canvas, js);
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

    private void OnBlur(Event e) => Mouse.Release();

    public void Dispose()
    {
        _window.OnBlur -= OnBlur;
        Keyboard.EscapePressed -= Mouse.Release;
        Mouse.PointerLockReleased -= Keyboard.Reset;
        Keyboard.Dispose();
        Mouse.Dispose();
        _window.Dispose();
    }
}
