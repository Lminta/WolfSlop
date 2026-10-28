# Engine UI

`WolfNET.Engine.UI` is a retained UI system, independent of game rules, Blazor
components, and the world renderer. Applications build a `UiDocument`, update its
controls from their state, and present its `UiFrame` through a backend. The browser
backend draws Canvas 2D over an existing canvas, including a WebGPU canvas.
It lives in the separate `WolfNET.Engine.Browser` project; the UI model, layout,
controls and input router remain in portable Engine. Android and native Windows
providers are future work; see [platform architecture](../PLATFORMS.md).

The same tree controls layout, drawing, hit testing, keyboard focus, and accessible
content. The game HUD, first-person art, and procedural viewer use this system.
Open `/ui` in Examples for an interactive control gallery.

## Build and present a document

```csharp
using System.Numerics;
using WolfNET.Engine.UI;
using WolfNET.Engine.Browser.UI;

using var document = new UiDocument { ScaleMode = UiScaleMode.Pixels };
var panel = document.Root.Add(new UiPanel("status")
{
    Style = new()
    {
        Position = new(16, 16), Width = 280, Padding = new(16),
        Layout = UiLayoutMode.Column, Gap = 10,
        Background = "#142232", CornerRadius = 8
    }
});
var label = panel.Add(new UiText("message") { Text = "Ready" });
var button = panel.Add(new UiButton("start") { Text = "Start" });
button.Clicked += _ => label.Text = "Running";

// In a Blazor WebAssembly component, canvas is its world canvas ElementReference.
using var ui = await BrowserUiHost.CreateAsync(jsRuntime, canvas, cancellationToken);
ui.SetDocument(document);
await ui.PrepareImagesAsync(cancellationToken);

// Call alongside the world renderer, including while gameplay is paused.
var viewport = ui.Viewport;
ui.Render(viewport.X, viewport.Y);
```

Keep these owners alive until the host component is disposed. A complete host is
[`UiGallery.razor`](https://github.com/Lminta/WolfNET/blob/dev/WolfNET.Examples/Pages/UiGallery.razor). Game-specific UI
composition is in [`ExampleHud.cs`](https://github.com/Lminta/WolfNET/blob/dev/WolfNET.Examples/Game/ExampleHud.cs).
`BrowserUiHost` uses in-process WebAssembly interop; it is not a Blazor Server
renderer. Its JavaScript is a provider asset at `_content/WolfNET.Engine.Browser/ui.js`.

## Controls

| Control | Behavior |
| --- | --- |
| `UiPanel` | Groups children, backgrounds, borders, anchored/stacked layout, clipping |
| `UiText` | Font, color, outline, alignment, multiline text, wrapping, ellipsis |
| `UiImage` | Asset URI, normalized atlas crop, stretch/contain/cover, transforms |
| `UiCanvas` | Cached custom vector display list; useful for maps, diagrams and charts |
| `UiButton` | Pointer and keyboard activation, hover/press/focus/disabled appearance |
| `UiToggle` | Button with checked state and change notifications |
| `UiSlider` | Captured pointer drag, arrow/Home/End keys, normalized value |
| `UiProgressBar` | A non-interactive normalized value display |
| `UiTextInput` | Single-line native browser editing, selection, IME, placeholder, maximum length, read-only/password modes, submission |
| `UiScrollView` | Vertical overflow, clipping, wheel input, and scrolling focused controls into view |

Use `TextChanged`, `ValueChanged`, `Changed`, and `Clicked` for application binding.
Updates are ordinary C# assignments; there is no dependency on a game's event bus.
Custom controls can derive from `UiElement` and compose existing controls and
`UiCanvas`. Containers and styles can be shared by using immutable `UiStyle` records.

## Layout and scaling

`UiStyle` sizes, margins, padding, and offsets use logical pixels. A `UiLength` can
be a pixel length, `Auto`, or `UiLength.Percent(fraction)`. Min/max sizes constrain
the result. `AspectRatio` can infer an automatic width or height.

An overlay container places children using `Anchor`, `Pivot`, and `Position`:
`Anchor = (.5, .5)` with `Pivot = (.5, .5)` centers a child; `(1, 1)` anchors a
child to the bottom-right. A row or column lays out flow children with padding,
margins, gaps, cross alignment, justification and proportional `Grow`. Set
`Absolute` to place a child by its anchors inside a row or column.

Percent sizes resolve against the parent's available size; provide a definite
size for a container when children depend on percentages. This layout model does
not implement the CSS flexbox algorithm.

`Rotation` uses radians; `Scale` and `TransformOrigin` apply after layout and are
inherited through the tree. `ZIndex` orders siblings stably. `Opacity` multiplies
through parents, and `Clip` clips descendants, including rounded corners. Hit
testing uses the same transforms and clips as drawing.

`UiScaleMode.Reference` uses a uniform scale from `ReferenceSize` (default
1920×1080), useful for authored screen art. `Pixels` keeps logical UI units equal
to the supplied viewport pixels. Supplying `BrowserUiHost.Viewport` gives CSS pixel
layout, independent of world render resolution. The host sizes the UI canvas for
device pixel ratio, capped at 4096 pixels per dimension, and tracks canvas resize.

The game uses CSS pixel layout for a readable HUD and anchor-based image placement
for hands and the cockpit. UI art is never part of the world map or depth buffer.

## Text, artwork and vector widgets

Fonts use `UiFont(Family, Size, Weight, Italic)`. The headless default measurer is
deterministic and approximate; `BrowserUiHost` supplies actual Canvas font metrics.
`LoadFontAsync(family, source)` loads an application font and invalidates cached
metrics. `InvalidateTextMetrics()` is available when fonts are loaded externally.
Wrapping preserves grapheme boundaries and respects `MaxLines`.
Text controls cache measured layouts. Custom text measurers should advance
`IUiTextMeasurer.Version` when their font metrics change; browser font loading
and `InvalidateTextMetrics()` do this automatically.

Images are keyed by asset URI. `PrepareImagesAsync` preloads them and fills in
intrinsic dimensions. Images added later load lazily; call preparation again when
their automatic intrinsic size is needed. Missing images use a magenta fallback.
`SourceRect` is a normalized `(left, top, width, height)` rectangle, so image atlases
can supply independently positioned and animated parts.

`UiDrawingBuilder` provides polylines, polygons, rectangles and circles with fills,
strokes, and affine transforms. Set `UiCanvas.ViewBox` to the vector coordinate
space. Assign a new immutable `UiDrawing` when the content changes. For scrolling
or rotating views, retain the drawing and update `DrawingTransform` instead of
rebuilding its points. Stroke widths remain independent of this transform:

```csharp
map.Drawing = new UiDrawingBuilder().Polyline(route, "#edf4f8", 3).Build();
// On view changes:
map.DrawingTransform = Matrix3x2.CreateRotation(heading);
```

The backend caches vector surfaces by control, drawing revision and raster size.
Unchanged drawing data is omitted from subsequent browser transfers. Documents
reuse unchanged display lists; font metrics and decoded images are cached too.
Animations update ordinary style/content properties from the application's loop.

## Input, accessibility and ownership

Nodes have stable, unique IDs and one parent. Tree mutation rejects duplicate IDs,
cycles and accidental double ownership. A document owns its attached tree;
`BrowserUiHost` owns its canvas, event subscriptions, image/vector caches, custom
fonts and accessibility elements, but does not dispose the supplied document.

The input router hit-tests in reverse draw order, captures a pressed pointer until
release/cancel, bubbles events through parents, and prevents UI clicks from reaching
game input. Disabled or hidden ancestors prevent interaction. `TabIndex` controls
keyboard order; Enter/Space activate buttons and toggles. `ModalRoot` confines input
and focus, focuses its first control and restores previous focus when closed.
Native text editors preserve browser selection, composition, caret and editing keys.

`Role`, `Label`, and the text/control state generate a semantic accessibility tree
with focusable controls. `ClassName` is an optional semantic/test hook, not a visual
CSS style. UI visuals come from the document. Dialogs and tables can be composed
using standard roles. The browser mirrors only the attached document and removes
its accessible content and event handlers when the host is disposed.

For another backend, call `UiDocument.BuildFrame(viewport, textMeasurer)` and consume
its ordered nodes through `IUiRenderer`. Layout and input remain usable in native
.NET applications and tests. Raw `ScreenOverlay` remains a low-level WebGPU API;
the examples' HUD and first-person art use the UI tree.
