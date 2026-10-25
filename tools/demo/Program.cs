using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Bg3PartyTool;
using Bg3PartyTool.Core;
using Bg3PartyTool.Views;

// usage: Demo <save.lsv|archive> <character to park> <frames dir>
// HOME/XDG_DATA_HOME should point at a scratch dir so only the demo save is listed.
var (src, who, outDir) = (args[0], args[1], args[2]);
Directory.CreateDirectory(outDir);
var imported = Library.Import(src);

AppBuilder.Configure<App>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia()
    .WithInterFont()
    .SetupWithoutStarting();

var win = new MainWindow();
win.Show();
int frame = 0;
void Pump(Func<bool> until, int timeoutMs = 60000)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (!until())
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(20);
        if (sw.ElapsedMilliseconds > timeoutMs) throw new Exception("timeout waiting for UI");
    }
    for (int i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(20); }
}
void Shot(Window w, string name)
{
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    Dispatcher.UIThread.RunJobs();
    using var bmp = w.CaptureRenderedFrame() ?? throw new Exception("no frame");
    var path = Path.Combine(outDir, $"{++frame:00}-{name}.png");
    bmp.Save(path);
    Console.WriteLine(path);
}
Button FindButton(Visual root, string text) =>
    root.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == text || (b.Content as TextBlock)?.Text == text);
void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

// 1. the list after startup
Pump(() => win.SaveList.ItemsSource is IEnumerable<ListBoxItem> items && items.Any(i => i.Tag is SaveEntry));
Shot(win, "start");

// 2. open the save
var pick = imported.FirstOrDefault(p => Path.GetFileName(p).StartsWith("QuickSave", StringComparison.OrdinalIgnoreCase)) ?? imported[0];
var item = ((IEnumerable<ListBoxItem>)win.SaveList.ItemsSource!).First(i => i.Tag is SaveEntry e && e.Path == pick);
win.SaveList.SelectedItem = item;
Pump(() => win.SaveView.IsVisible && win.Cards.Children.Count > 0);
Shot(win, "save");

// 3. park dialog
var card = win.Cards.Children.First(c => c.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == who));
Click(FindButton(card, L.T("card.park")));
Pump(() => win.OwnedWindows.Count > 0);
var dlg = win.OwnedWindows[0];
Shot(dlg, "park-dialog");

// 4. confirm, wait for the result
Click(FindButton(dlg, L.T("park.ok")));
Pump(() => win.ResultPanel.IsVisible);
win.MainScroll.Offset = new Vector(0, Math.Max(0, (win.ResultPanel.TranslatePoint(new Point(0, 0), win.MainPanel)?.Y ?? 0) - 24));
Pump(() => true);
Shot(win, "parked");

// 5. open the new save: the parked character shows up there
Click(win.ShowResultButton);
Pump(() => win.ParkedCards.Children.Count > 0);
Shot(win, "newsave");

// 6. restore dialog
Click(FindButton(win.ParkedCards.Children[0], L.T("parked.restore")));
Pump(() => win.OwnedWindows.Count > 0);
Shot(win.OwnedWindows[0], "restore-dialog");
Click(FindButton(win.OwnedWindows[0], L.T("cancel")));
Pump(() => win.OwnedWindows.Count == 0);
Console.WriteLine("done");
