using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Bg3PartyTool;
using Bg3PartyTool.Core;
using Bg3PartyTool.Views;
using SkiaSharp;

// Renders a screencast of the tool without a display: the real UI runs on Avalonia's headless backend, a synthetic
// cursor glides between controls and clicks them with genuine pointer events, and every tick is written as a PNG frame.
// usage: Demo <save.lsv|archive> <character to park> <frames dir>
// HOME/XDG_DATA_HOME should point at a scratch dir so only the demo save is listed.
var (src, who, outDir) = (args[0], args[1], args[2]);
Directory.CreateDirectory(outDir);
foreach (var f in Directory.EnumerateFiles(outDir, "*.png")) File.Delete(f);
var imported = Library.Import(src);

AppBuilder.Configure<App>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia()
    .WithInterFont()
    .SetupWithoutStarting();

const int Fps = 20;
var win = new MainWindow();
win.Show();
var rec = new Recorder(win, outDir, Fps);

// 1. the list after startup
rec.WaitFor(() => win.SaveList.ItemsSource is IEnumerable<ListBoxItem> items && items.Any(i => i.Tag is SaveEntry));
rec.CursorAt(new Point(700, 420));
rec.Hold(1.0);

// 2. open the save
var pick = imported.FirstOrDefault(p => Path.GetFileName(p).StartsWith("QuickSave", StringComparison.OrdinalIgnoreCase)) ?? imported[0];
var item = ((IEnumerable<ListBoxItem>)win.SaveList.ItemsSource!).First(i => i.Tag is SaveEntry e && e.Path == pick);
rec.MoveTo(win, item, 0.9);
rec.Click(win, item);
rec.WaitFor(() => win.SaveView.IsVisible && win.Cards.Children.Count > 0);
rec.Hold(1.8);

// 3. park the character
var card = win.Cards.Children.First(c => c.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == who));
var park = Find(card, L.T("card.park"));
rec.MoveTo(win, park, 1.0);
rec.Hold(0.4);
rec.Click(win, park);
rec.WaitFor(() => win.OwnedWindows.Count > 0);
var dlg = win.OwnedWindows[0];
rec.Dialog = dlg;
rec.Hold(0.3);
rec.CursorAt(rec.DialogOrigin + new Point(dlg.Bounds.Width / 2, dlg.Bounds.Height / 2));
rec.Hold(2.2);
var ok = Find(dlg, L.T("park.ok"));
rec.MoveTo(dlg, ok, 0.9);
rec.Hold(0.3);
rec.Click(dlg, ok);
rec.Dialog = null;
rec.WaitFor(() => win.ResultPanel.IsVisible, capture: true);  // the busy overlay is part of the show
rec.ScrollTo(win.ResultPanel, 0.8);
rec.Hold(2.6);

// 4. open the new save: the parked character shows up there
rec.MoveTo(win, win.ShowResultButton, 0.8);
rec.Click(win, win.ShowResultButton);
rec.WaitFor(() => win.ParkedCards.Children.Count > 0);
rec.Hold(1.6);

// 5. restore it
var restore = Find(win.ParkedCards.Children[0], L.T("parked.restore"));
rec.ScrollTo(win.ParkedCards, 0.7, margin: 120);
rec.MoveTo(win, restore, 0.9);
rec.Hold(0.4);
rec.Click(win, restore);
rec.WaitFor(() => win.OwnedWindows.Count > 0);
dlg = win.OwnedWindows[0];
rec.Dialog = dlg;
rec.Hold(0.3);
rec.CursorAt(rec.DialogOrigin + new Point(dlg.Bounds.Width / 2, dlg.Bounds.Height / 2));
rec.Hold(2.0);
ok = Find(dlg, L.T("restore.ok"));
rec.MoveTo(dlg, ok, 0.9);
rec.Hold(0.3);
rec.Click(dlg, ok);
rec.Dialog = null;
rec.WaitFor(() => win.ResultPanel.IsVisible && win.ResultTitle.Text == L.F("done.restore", who), capture: true);
rec.ScrollTo(win.ResultPanel, 0.8);
rec.Hold(3.0);
Console.WriteLine($"{rec.Frames} frames");

static Button Find(Visual root, string text) =>
    root.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == text || (b.Content as TextBlock)?.Text == text);

/// <summary>Drives the headless windows and writes composited frames (main window, dimmed with a centred dialog when
/// one is open, plus the cursor and a click ripple).</summary>
sealed class Recorder(Window main, string dir, int fps)
{
    public int Frames { get; private set; }
    public Window? Dialog { get; set; }
    Point _cursor = new(600, 400);
    int _ripple = -1;

    public Point DialogOrigin => Dialog == null ? default
        : new Point((main.Bounds.Width - Dialog.Bounds.Width) / 2, (main.Bounds.Height - Dialog.Bounds.Height) / 2);

    void Tick()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    public void WaitFor(Func<bool> until, bool capture = false, int timeoutMs = 90000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!until())
        {
            Tick();
            // show the busy overlay for a moment, then wait off-camera: a 20 s repack is not worth 400 identical frames
            if (capture && sw.ElapsedMilliseconds < 1500) Frame(); else Thread.Sleep(1000 / fps);
            if (sw.ElapsedMilliseconds > timeoutMs) throw new Exception("timeout waiting for UI");
        }
        for (int i = 0; i < 4; i++) Tick();
    }

    public void Hold(double seconds)
    {
        for (int i = 0; i < seconds * fps; i++) { Tick(); Frame(); }
    }

    public void CursorAt(Point p) => _cursor = p;

    /// <summary>Glide the cursor to the centre of a control with ease-in-out, sending pointer moves so hover styles react.</summary>
    public void MoveTo(Window w, Control target, double seconds)
    {
        var to = Centre(w, target);
        var from = _cursor;
        int n = Math.Max(2, (int)(seconds * fps));
        for (int i = 1; i <= n; i++)
        {
            var t = (double)i / n;
            t = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
            _cursor = from + (to - from) * t;
            w.MouseMove(Local(w, _cursor));
            Tick();
            Frame();
        }
    }

    public void Click(Window w, Control target)
    {
        _cursor = Centre(w, target);
        var local = Local(w, _cursor);
        w.MouseMove(local);
        w.MouseDown(local, MouseButton.Left);
        _ripple = 0;
        Tick(); Frame();
        w.MouseUp(local, MouseButton.Left);
        Tick(); Frame();
    }

    /// <summary>Smoothly scroll the main view so the control sits near the top.</summary>
    public void ScrollTo(Control target, double seconds, double margin = 24)
    {
        var mw = (MainWindow)main;
        var y = target.TranslatePoint(new Point(0, 0), mw.MainPanel)?.Y ?? 0;
        var goal = Math.Clamp(y - margin, 0, Math.Max(0, mw.MainScroll.Extent.Height - mw.MainScroll.Viewport.Height));
        var start = mw.MainScroll.Offset.Y;
        int n = Math.Max(2, (int)(seconds * fps));
        for (int i = 1; i <= n; i++)
        {
            var t = (double)i / n;
            t = 1 - Math.Pow(1 - t, 3);
            mw.MainScroll.Offset = new Vector(0, start + (goal - start) * t);
            Tick(); Frame();
        }
    }

    Point Centre(Window w, Control c)
    {
        var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w) ?? throw new Exception("control not visual");
        return w == Dialog ? DialogOrigin + p : p;
    }

    Point Local(Window w, Point composite) => w == Dialog ? composite - DialogOrigin : composite;

    void Frame()
    {
        if (Dialog is { IsVisible: false }) Dialog = null; // closed by the click that was just sent
        using var mainBmp = Capture(main);
        using var canvas = new SKCanvas(mainBmp);
        if (Dialog != null)
        {
            canvas.DrawRect(0, 0, mainBmp.Width, mainBmp.Height, new SKPaint { Color = new SKColor(0, 0, 0, 140) });
            using var dlgBmp = Capture(Dialog);
            var o = DialogOrigin;
            canvas.DrawRect((float)o.X - 1, (float)o.Y - 1, dlgBmp.Width + 2, dlgBmp.Height + 2, new SKPaint { Color = new SKColor(0x35, 0x2E, 0x42) });
            canvas.DrawBitmap(dlgBmp, (float)o.X, (float)o.Y);
        }
        if (_ripple >= 0)
        {
            var r = 6 + _ripple * 4;
            canvas.DrawCircle((float)_cursor.X, (float)_cursor.Y, r, new SKPaint { Color = new SKColor(0xD4, 0xAF, 0x6A, (byte)Math.Max(0, 170 - _ripple * 28)), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3 });
            if (++_ripple > 6) _ripple = -1;
        }
        DrawCursor(canvas, _cursor);
        using var img = SKImage.FromBitmap(mainBmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        using var fs = File.Create(Path.Combine(dir, $"{++Frames:D4}.png"));
        data.SaveTo(fs);
    }

    static SKBitmap Capture(Window w)
    {
        using var frame = w.CaptureRenderedFrame() ?? throw new Exception("no frame");
        using var ms = new MemoryStream();
        frame.Save(ms);
        return SKBitmap.Decode(ms.ToArray());
    }

    static void DrawCursor(SKCanvas c, Point p)
    {
        const float s = 1.7f;
        var path = new SKPath();
        path.MoveTo(0, 0); path.LineTo(0, 16 * s); path.LineTo(4 * s, 12.5f * s); path.LineTo(7 * s, 19 * s);
        path.LineTo(9.5f * s, 18 * s); path.LineTo(6.5f * s, 11.5f * s); path.LineTo(11 * s, 11.5f * s); path.Close();
        path.Transform(SKMatrix.CreateTranslation((float)p.X, (float)p.Y));
        c.DrawPath(path, new SKPaint { Color = SKColors.White, IsAntialias = true, Style = SKPaintStyle.Fill });
        c.DrawPath(path, new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f });
    }
}
