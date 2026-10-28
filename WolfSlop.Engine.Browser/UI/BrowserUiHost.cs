using System.Numerics;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using WolfNET.Engine.UI;

namespace WolfNET.Engine.Browser.UI;

/// <summary>Canvas 2D UI presentation and accessible input over an application's existing canvas.
/// Owns browser resources, not the supplied document. Requires Blazor WebAssembly.</summary>
public sealed class BrowserUiHost : IUiRenderer, IUiTextMeasurer
{
    private IJSObjectReference? _module;
    private IJSInProcessObjectReference? _surface;
    private DotNetObjectReference<BrowserUiHost>? _callback;
    private readonly Dictionary<(string, UiFont), float> _measurements = [];
    private readonly Dictionary<string, long> _drawingVersions = new(StringComparer.Ordinal);
    private UiFrame? _lastFrame;
    private Vector2 _viewport;
    private bool _disposed;
    public UiDocument? Document { get; private set; }
    public Vector2 Viewport
    {
        get { var size = _surface!.Invoke<float[]>("size"); return new(size[0], size[1]); }
    }

    private BrowserUiHost() { }
    public static async Task<BrowserUiHost> CreateAsync(IJSRuntime runtime, ElementReference canvas,
        CancellationToken cancellationToken = default)
    {
        var host = new BrowserUiHost();
        try
        {
            host._module = await runtime.InvokeAsync<IJSObjectReference>("import", cancellationToken, "./_content/WolfNET.Engine.Browser/ui.js");
            host._callback = DotNetObjectReference.Create(host);
            var reference = await host._module.InvokeAsync<IJSObjectReference>("createUi", cancellationToken, canvas, host._callback);
            host._surface = reference as IJSInProcessObjectReference ?? throw new NotSupportedException("BrowserUiHost requires in-process WebAssembly interop.");
            return host;
        }
        catch { host.Dispose(); throw; }
    }

    public void SetDocument(UiDocument? document)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Document?.Input.Cancel();
        Document = document; _lastFrame = null; _drawingVersions.Clear();
        _surface!.InvokeVoid("reset");
    }

    public async Task PrepareImagesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var document = Document;
        if (document == null) return;
        var images = await _surface!.InvokeAsync<ImageInfo[]>("prepare", cancellationToken, (object)document.ImageSources.ToArray());
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed || !ReferenceEquals(document, Document)) return;
        var sizes = images.ToDictionary(image => image.Source);
        foreach (var image in document.Root.DescendantsAndSelf().OfType<UiImage>())
            if (sizes.TryGetValue(image.Source, out var size) && size.Width > 0 && size.Height > 0)
                image.IntrinsicSize = new(size.Width, size.Height);
    }

    public void Render(float width, float height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _viewport = new(width, height);
        if (Document == null) return;
        Render(Document.BuildFrame(_viewport, this));
    }

    public void Render(UiFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        if (ReferenceEquals(_lastFrame, frame)) return;
        var nodes = frame.Nodes.Select(node =>
        {
            if (node.Drawing == null) return node;
            var previous = _drawingVersions.GetValueOrDefault(node.Id);
            _drawingVersions[node.Id] = node.Drawing.Revision;
            return previous == node.Drawing.Revision ? node with { Drawing = null } : node;
        }).ToArray();
        var ids = frame.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _drawingVersions.Keys.Where(id => !ids.Contains(id)).ToArray()) _drawingVersions.Remove(id);
        _surface!.InvokeVoid("render", frame with { Nodes = nodes });
        _lastFrame = frame;
    }

    public float MeasureWidth(string text, UiFont font)
    {
        if (_measurements.TryGetValue((text, font), out var width)) return width;
        width = _surface!.Invoke<float>("measure", text, font);
        if (_measurements.Count >= 4096) _measurements.Clear();
        _measurements[(text, font)] = width;
        return width;
    }

    public void InvalidateTextMetrics()
    {
        _measurements.Clear(); Document?.Invalidate();
    }
    public async Task LoadFontAsync(string family, string source, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _surface!.InvokeVoidAsync("loadFont", cancellationToken, family, source);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_disposed) InvalidateTextMetrics();
    }

    [JSInvokable]
    public bool Dispatch(UiBrowserEvent e)
    {
        if (_disposed || Document == null) return false;
        var input = Document.Input;
        var point = new Vector2(e.X, e.Y);
        var handled = e.Kind switch
        {
            "down" => input.PointerDown(point, e.PointerId),
            "move" => input.PointerMove(point, e.PointerId),
            "up" => input.PointerUp(point, e.PointerId),
            "wheel" => input.Wheel(point, e.Delta),
            "key" => input.KeyDown(e.Key ?? "", e.Shift, e.Repeat),
            "activate" => e.Id != null && input.Activate(e.Id),
            "focus" => e.Id != null && input.Focus(Document.Find(e.Id)),
            "text" => e.Id != null && input.TextInput(e.Id, e.Value ?? ""),
            _ => false
        };
        if (e.Kind == "cancel") input.Cancel();
        if (!_disposed && _viewport.X > 0 && _viewport.Y > 0) Render(_viewport.X, _viewport.Y);
        return handled;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Document?.Input.Cancel(); Document = null;
        _surface?.InvokeVoid("dispose");
        _surface?.Dispose(); _surface = null;
        if (_module is IJSInProcessObjectReference module) module.Dispose();
        else if (_module != null) _ = _module.DisposeAsync();
        _module = null; _callback?.Dispose(); _callback = null;
        _measurements.Clear(); _drawingVersions.Clear(); _lastFrame = null;
    }

    public sealed record ImageInfo(string Source, float Width, float Height);
}

public sealed record UiBrowserEvent(string Kind, float X = 0, float Y = 0, int PointerId = 0,
    string? Key = null, string? Id = null, bool Shift = false, bool Repeat = false, float Delta = 0, string? Value = null);
