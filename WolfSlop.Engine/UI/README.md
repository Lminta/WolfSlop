# Engine UI

`WolfSlop.Engine.UI` is a retained, platform-independent UI model. Applications build a `UiDocument`, update controls, and call `BuildFrame(viewport, textMeasurer)` to produce ordered draw commands.

The native `SdlUiRenderer` supplies SkiaSharp font metrics and draws the resulting frame. The SDL world renderer composites its bitmap over the scene. Viewports and pointer coordinates use the same logical units; the game uses SDL window coordinates so display scaling remains consistent.

Controls include panels, text, buttons, toggles, sliders/progress bars, images, vector canvases, and scroll views. `UiStyle` controls sizes, anchors/pivots, row/column layout, padding, color, borders, rounded clipping, opacity, rotation, and scale. `UiDrawingBuilder` creates immutable rectangles, circles, polygons, and polylines. Canvas view-box scaling precedes the drawing transform.

Route pointer down/move/up, wheel, and keyboard navigation through `document.Input`. `ModalRoot` confines focus and hit testing to a dialog. Focus loss should cancel pointer capture and held game controls. Native editor/IME and OS accessibility services require additional provider integration; descriptive roles and labels remain portable metadata.

The game owns HUD composition and menu behavior. UI controls do not own game state or GPU handles. Dispose the document and provider when leaving the application.
