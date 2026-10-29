using System.Numerics;
using WolfSlop.Engine.UI;

internal static class UiChecks
{
    public static int Run()
    {
        var count = 0;
        void Check(bool condition, string message) { count++; if (!condition) throw new Exception(message); }
        void Reject<T>(Action action) where T : Exception
        {
            try { action(); } catch (T) { count++; return; }
            throw new Exception("Expected " + typeof(T).Name);
        }
        using var document = new UiDocument { ScaleMode = UiScaleMode.Pixels };
        var column = document.Root.Add(new UiPanel("column") { Style = new()
        {
            Position = new(10, 20), Width = 300, Height = 160, Padding = new(10),
            Layout = UiLayoutMode.Column, Gap = 5, Alignment = UiAlignment.Stretch
        } });
        var label = column.Add(new UiText("label") { Text = "One line", Font = new(Size: 10) });
        var first = column.Add(new UiButton("first") { Text = "First", Style = new() { Height = 30 } });
        var second = column.Add(new UiButton("second") { Text = "Second", Style = new() { Height = 30 } });
        var frame = document.BuildFrame(new(800, 600));
        Check(first.Bounds.Width == 280 && first.Bounds.Y > label.Bounds.Y, "Stack layout did not account for padding, text and stretch.");
        Check(ReferenceEquals(frame, document.BuildFrame(new(800, 600))), "Unchanged documents must reuse their display list.");
        label.Text = "Changed";
        Check(!ReferenceEquals(frame, document.BuildFrame(new(800, 600))), "Text changes did not invalidate the display list.");
        var target = Vector2.Transform(new(5), first.WorldTransform);
        var clicks = 0;
        first.Clicked += _ => clicks++;
        Check(document.Input.PointerDown(target), "UI button did not capture pointer input.");
        document.Input.PointerUp(target);
        Check(clicks == 1 && first.IsFocused, "A release inside a button must activate and focus it.");
        document.Input.PointerDown(target); document.Input.PointerUp(new(790, 590));
        Check(clicks == 1 && !first.IsPressed, "Releasing outside a captured button activated it or retained capture.");
        document.Input.PointerDown(target); document.Input.Cancel(); document.Input.PointerUp(target);
        Check(clicks == 1, "Cancelled pointer input activated a button.");
        var updating = document.Root.Add(new UiPanel("updating") { Style = new() { Position = new(700, 500), Width = 50, Height = 50 } });
        updating.Add(new UiText("updating-label") { Text = "Status" });
        document.BuildFrame(new(800, 600));
        document.Input.PointerDown(target); updating.Children.Clear(); document.BuildFrame(new(800, 600)); document.Input.PointerUp(target);
        Check(clicks == 2, "An unrelated widget update cancelled a captured button click.");
        document.Input.Focus(first); document.Input.KeyDown("Enter"); document.Input.KeyDown("Enter", repeat: true);
        Check(clicks == 3, "Keyboard activation repeated while the key was held.");
        document.Input.MoveFocus();
        Check(second.IsFocused, "Tab order did not advance to the next button.");
        document.Input.MoveFocus(backwards: true);
        Check(first.IsFocused, "Reverse Tab order failed.");
        column.Enabled = false; document.BuildFrame(new(800, 600));
        Check(document.Input.Focused == null && !document.Input.Activate(first.Id), "Disabled ancestors must prevent focus and activation.");
        column.Enabled = true;

        var modal = document.Root.Add(new UiPanel("modal") { Style = new() { Position = new(400, 100), Width = 200, Height = 200, Clip = true } });
        var modalButton = modal.Add(new UiButton("modal-button") { Text = "Modal", Style = new() { Width = 100, Height = 40 } });
        document.ModalRoot = modal; document.BuildFrame(new(800, 600));
        Check(!document.Input.Focus(first) && document.Input.MoveFocus() && modalButton.IsFocused, "Modal focus escaped its subtree.");
        Check(document.Input.PointerDown(new(20, 20)), "Modal background allowed input to reach the game.");
        document.Input.Cancel(); document.ModalRoot = null;
        document.Input.Focus(first); document.ModalRoot = modal;
        Check(modalButton.IsFocused, "Opening a modal did not focus its first control.");
        modal.Visible = false; document.BuildFrame(new(800, 600));
        Check(document.ModalRoot == null && first.IsFocused, "Hiding a modal did not release its scope and restore focus.");
        modal.Visible = true;

        var clipped = document.Root.Add(new UiPanel("clip") { Style = new() { Position = new(450, 350), Width = 40, Height = 40, Clip = true } });
        var clippedButton = clipped.Add(new UiButton("clipped-button") { Text = "", Style = new() { Position = new(30, 0), Width = 80, Height = 30 } });
        var clippedClicks = 0; clippedButton.Clicked += _ => clippedClicks++;
        document.BuildFrame(new(800, 600));
        Check(!document.Input.PointerDown(new(510, 360)), "Clipped content remained interactive outside its parent.");
        document.Input.PointerDown(new(485, 360)); document.Input.PointerUp(new(485, 360));
        Check(clippedClicks == 1, "Visible portion of clipped button was not interactive.");

        var transformed = document.Root.Add(new UiButton("rotated") { Text = "", Style = new()
            { Position = new(600, 100), Width = 100, Height = 40, Rotation = MathF.PI / 2, Scale = new(1.5f), ZIndex = 20 } });
        var transformedClicks = 0; transformed.Clicked += _ => transformedClicks++;
        document.BuildFrame(new(800, 600));
        var rotatedPoint = Vector2.Transform(new(20, 20), transformed.WorldTransform);
        document.Input.PointerDown(rotatedPoint); document.Input.PointerUp(rotatedPoint);
        Check(transformedClicks == 1, "Hit testing did not invert the element's rotation and scale.");

        var scroll = document.Root.Add(new UiScrollView("scroll") { Style = new()
            { Position = new(10, 300), Width = 150, Height = 50, Layout = UiLayoutMode.Column, Clip = true } });
        for (var i = 0; i < 6; i++) scroll.Add(new UiText("scroll-" + i) { Text = i.ToString(), Style = new() { Height = 20 } });
        document.BuildFrame(new(800, 600));
        Check(scroll.Extent == 70 && document.Input.Wheel(new(20, 320), 100), "Scrolling did not compute and consume overflow.");
        document.BuildFrame(new(800, 600));
        Check(scroll.Offset == 70 && scroll.Children[0].Bounds.Y == -70, "Scroll offset did not clamp and move its contents.");

        var slider = document.Root.Add(new UiSlider("slider") { Style = new() { Position = new(200, 300), Width = 100, Height = 20 } });
        document.BuildFrame(new(800, 600));
        document.Input.PointerDown(new(225, 310)); document.Input.PointerMove(new(280, 310)); document.Input.PointerUp(new(280, 310));
        Check(Math.Abs(slider.Value - .8f) < .0001, "Captured slider drag did not update its normalized value.");
        document.Input.KeyDown("End"); Check(slider.Value == 1, "Keyboard slider control failed.");
        var toggle = document.Root.Add(new UiToggle("toggle") { Text = "Option" });
        document.Input.Activate(toggle.Id); Check(toggle.Checked, "Toggle activation did not update its value.");
        var editor = document.Root.Add(new UiTextInput("editor") { MaxLength = 4 });
        document.Input.TextInput(editor.Id, "ab😀z");
        Check(editor.Text == "ab😀", "Text input limits must preserve surrogate pairs.");
        editor.ReadOnly = true;
        Check(!document.Input.TextInput(editor.Id, "x") && editor.Text == "ab😀", "Read-only text input accepted an edit.");
        editor.ReadOnly = false; document.Input.Focus(editor);
        var submitted = ""; editor.Submitted += value => submitted = value;
        document.Input.KeyDown("Enter"); Check(submitted == editor.Text, "Text input did not submit on Enter.");
        editor.Password = true;
        Check(document.BuildFrame(new(800, 600)).Nodes.Single(node => node.Id == editor.Id).Content == "••••",
            "Password input did not mask its canvas presentation.");

        var lines = UiTextLayout.Measure("alpha beta gamma", new(Size: 10), 34, 2, UiApproximateTextMeasurer.Instance);
        Check(lines.Lines.Count == 2 && lines.Lines[^1].Text.EndsWith('…'), "Wrapped text did not apply its line limit and ellipsis.");
        var unicode = UiTextLayout.Measure("a\u0301😀", new(Size: 10), 6, 20, UiApproximateTextMeasurer.Instance);
        Check(unicode.Lines.Count == 2 && unicode.Lines[0].Text == "a\u0301" && unicode.Lines[1].Text == "😀", "Text wrapping split Unicode graphemes.");
        var aspect = document.Root.Add(new UiImage("aspect") { Source = "image.png", Style = new() { Width = 200, AspectRatio = 2 } });
        document.BuildFrame(new(800, 600));
        Check(aspect.Bounds.Height == 100, "Image aspect layout did not infer height.");
        var canvas = document.Root.Add(new UiCanvas("course")
        {
            Drawing = new UiDrawingBuilder().Polyline([Vector2.Zero, Vector2.One], "#fff", 2).Build()
        });
        var original = document.BuildFrame(new(800, 600));
        canvas.DrawingTransform = Matrix3x2.CreateRotation(.3f) * Matrix3x2.CreateTranslation(5, 8);
        var moved = document.BuildFrame(new(800, 600));
        var before = original.Nodes.Single(node => node.Id == canvas.Id);
        var after = moved.Nodes.Single(node => node.Id == canvas.Id);
        Check(moved.Version > original.Version && ReferenceEquals(before.Drawing, after.Drawing) &&
            after.DrawingTransform.Matrix == canvas.DrawingTransform,
            "Moving a drawing must publish its transform without rebuilding immutable geometry.");
        using var cached = new UiDocument { ScaleMode = UiScaleMode.Pixels };
        cached.Root.Add(new UiText("cached-text") { Text = "speed" });
        var metrics = new CountingMetrics();
        var firstLayout = cached.BuildFrame(new(800, 600), metrics).Nodes.Single(node => node.Id == "cached-text").TextLayout!;
        var measurements = metrics.Count;
        cached.Invalidate();
        var reusedLayout = cached.BuildFrame(new(800, 600), metrics).Nodes.Single(node => node.Id == "cached-text").TextLayout!;
        Check(ReferenceEquals(firstLayout, reusedLayout) && metrics.Count == measurements,
            "Unchanged HUD text must reuse layout across display-list updates.");
        metrics.Version++; metrics.Scale = 2;
        cached.Invalidate();
        var refreshed = cached.BuildFrame(new(800, 600), metrics).Nodes.Single(node => node.Id == "cached-text").TextLayout!;
        Check(refreshed.Width == firstLayout.Width * 2 && metrics.Count > measurements,
            "Loading a font must invalidate cached layout and text measurements.");
        using var scaled = new UiDocument { ReferenceSize = new(800, 600) };
        var scaledFrame = scaled.BuildFrame(new(400, 300));
        Check(scaledFrame.Width == 800 && scaledFrame.Height == 600, "Reference scaling changed logical layout size.");

        Reject<InvalidOperationException>(() => document.Root.Add(new UiText(first.Id)));
        Reject<InvalidOperationException>(() => column.Children.Add(first));
        Reject<InvalidOperationException>(() => column.Children.Add(document.Root));
        document.Input.Focus(first); column.Children.Remove(first);
        Check(first.Document == null && first.Parent == null && document.Input.Focused == null, "Detaching a node retained focus or ownership.");
        Reject<ArgumentOutOfRangeException>(() => document.BuildFrame(new(float.NaN, 2)));
        using var disposable = new UiDocument();
        var closing = disposable.Root.Add(new UiButton("close") { Text = "Close" });
        closing.Clicked += _ => disposable.Dispose();
        Check(disposable.Input.Activate(closing.Id) && disposable.IsDisposed, "A click cannot safely close its own document.");
        Reject<ObjectDisposedException>(() => disposable.BuildFrame(new(100)));
        Console.WriteLine($"PASS: {count} UI assertions (layout, text, transforms, clipping, input, focus, modal, controls, scrolling, and lifetime).");
        return count;
    }
    private sealed class CountingMetrics : IUiTextMeasurer
    {
        public long Version { get; set; }
        public int Count;
        public float Scale = 1;
        public float MeasureWidth(string text, UiFont font) { Count++; return text.Length * font.Size * Scale; }
    }
}
