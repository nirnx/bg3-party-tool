using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Bg3PartyTool.Core;

namespace Bg3PartyTool.Views;

public partial class MainWindow : Window
{
    static readonly Color[] Palette =
        new[] { "#D4AF6A", "#8D7BD9", "#5FB3A8", "#D97B8F", "#7FA7E0", "#C9895A" }.Select(Color.Parse).ToArray();
    static readonly (string code, string label)[] Languages = [("en", "English"), ("pl", "Polski")];

    List<SaveEntry> _saves = new();
    string _filter = "all";
    SaveFile? _current;
    List<ExportFile> _exports = new();
    RemoveResult? _lastResult;
    bool _updatingList;
    DispatcherTimer? _toastTimer;

    public MainWindow()
    {
        InitializeComponent();
        foreach (var chip in Filters.Children.OfType<ToggleButton>())
            chip.Click += (_, _) => SetFilter((string)chip.Tag!);
        SaveList.SelectionChanged += (_, _) =>
        {
            if (!_updatingList && SaveList.SelectedItem is ListBoxItem { Tag: SaveEntry e }) _ = OpenSave(e.Path);
        };
        DropZone.PointerPressed += async (_, _) => await PickFile();
        AddButton.Click += async (_, _) => await PickFile();
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
            DropZone.BorderBrush = Brush("Gold");
        });
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropZone.BorderBrush = Brush("Line"));
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            DropZone.BorderBrush = Brush("Line");
            if (e.DataTransfer.TryGetFile()?.TryGetLocalPath() is { } path) await ImportFile(path);
        });
        OpenFolderButton.Click += (_, _) => { if (_lastResult != null) RevealFolder(Path.GetDirectoryName(_lastResult.Output)!); };
        ShowResultButton.Click += async (_, _) => { if (_lastResult != null) await RefreshSaves(_lastResult.Output); };
        LoadCharacterButton.Click += async (_, _) => await PickCharacter();
        RenameButton.Click += async (_, _) => await AskRename();
        DeleteButton.Click += async (_, _) => await AskDelete();
        RevealButton.Click += (_, _) => { if (_current != null) RevealFolder(Path.GetDirectoryName(_current.LsvPath)!); };

        LangBox.ItemsSource = Languages.Select(l => l.label).ToList();
        LangBox.SelectedIndex = Math.Max(0, Array.FindIndex(Languages, l => l.code == L.Lang));
        LangBox.SelectionChanged += (_, _) => { if (LangBox.SelectedIndex >= 0) L.Set(Languages[LangBox.SelectedIndex].code); };
        L.Changed += ApplyLanguage;
        ApplyLanguage();
        Opened += async (_, _) => await RefreshSaves();
    }

    static IBrush Brush(string key) => (IBrush)Application.Current!.FindResource(key)!;

    /// <summary>Set every static caption from the string table and redraw the dynamic parts.</summary>
    void ApplyLanguage()
    {
        AppSubtitle.Text = L.T("app.subtitle");
        DropA.Text = L.T("drop.line1a");
        DropB.Text = L.T("drop.line1b");
        DropC.Text = L.T("drop.line2");
        AddButton.Content = L.T("add.disk");
        var chips = Filters.Children.OfType<ToggleButton>().ToList();
        chips[0].Content = L.T("filter.all");
        chips[1].Content = L.T("filter.imported");
        chips[2].Content = L.T("filter.game");
        chips[3].Content = L.T("filter.output");
        EmptyTitle.Text = L.T("empty.title");
        EmptyText.Text = L.T("empty.text");
        RenameButton.Content = L.T("save.rename");
        RevealButton.Content = L.T("save.reveal");
        DeleteButton.Content = L.T("save.delete");
        AvatarsTitle.Text = L.T("avatars.title");
        AvatarsHint.Text = L.T("avatars.hint");
        LoadCharacterButton.Content = L.T("parked.load");
        ParkedTitle.Text = L.T("parked.title");
        ParkedHint.Text = L.T("parked.hint");
        ParkedEmpty.Text = L.T("parked.empty");
        OpenFolderButton.Content = L.T("result.open");
        ShowResultButton.Content = L.T("result.show");
        HelpLabel.Text = L.T("help.label");
        HelpText.Text = L.T("help.text");
        BusyLabel.Text = L.T("busy");
        RenderSaveList();
        if (_current != null) RenderSave();
    }

    // --- save list --------------------------------------------------------------------------------------------------

    async Task RefreshSaves(string? select = null)
    {
        _saves = await Task.Run(Library.ListSaves);
        if (select != null) EnsureVisible(select);
        RenderSaveList();
        if (select != null) await OpenSave(select);
    }

    /// <summary>If the current filter would hide this save, fall back to "all" so it shows up in the list.</summary>
    void EnsureVisible(string path)
    {
        var entry = _saves.FirstOrDefault(e => e.Path == path);
        if (entry != null && _filter != "all" && entry.Source.ToString() != _filter) SetFilter("all");
    }

    void SetFilter(string filter)
    {
        _filter = filter;
        foreach (var chip in Filters.Children.OfType<ToggleButton>()) chip.IsChecked = (string)chip.Tag! == filter;
        RenderSaveList();
    }

    void RenderSaveList()
    {
        _updatingList = true;
        var items = new List<ListBoxItem>();
        foreach (var s in _saves.Where(s => _filter == "all" || s.Source.ToString() == _filter))
        {
            var (label, color) = s.Source switch
            {
                SaveSource.Game => (L.T("badge.game"), "Muted"),
                SaveSource.Imported => (L.T("badge.imported"), "Gold2"),
                _ => (L.T("badge.output"), "Ok"),
            };
            var thumb = new Border { Width = 72, Height = 46, CornerRadius = new CornerRadius(8), ClipToBounds = true, Background = Brushes.Black, Child = LoadImage(s.Thumb) };
            var badge = new Border
            {
                CornerRadius = new CornerRadius(99), BorderThickness = new Thickness(1), BorderBrush = Brush("Line"), Padding = new Thickness(7, 0),
                Child = new TextBlock { Text = label, FontSize = 10.5, Foreground = Brush(color) },
            };
            var meta = new WrapPanel();
            meta.Children.Add(new TextBlock { Text = $"{s.Leader} · {s.Modified:yyyy-MM-dd HH:mm}  ", FontSize = 12, Foreground = Brush("Muted"), VerticalAlignment = VerticalAlignment.Center });
            meta.Children.Add(badge);
            var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = s.Name, FontWeight = FontWeight.SemiBold, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(meta);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            row.Children.Add(thumb);
            Grid.SetColumn(text, 1);
            text.Margin = new Thickness(12, 0, 0, 0);
            row.Children.Add(text);
            ToolTip.SetTip(row, s.Path);
            items.Add(new ListBoxItem { Content = row, Tag = s });
        }
        if (items.Count == 0)
            items.Add(new ListBoxItem { IsEnabled = false, Content = new TextBlock { Text = L.T("list.empty"), Foreground = Brush("Muted"), FontSize = 13 } });
        SaveList.ItemsSource = items;
        SaveList.SelectedItem = items.FirstOrDefault(i => i.Tag is SaveEntry e && _current != null && e.Path == _current.LsvPath);
        _updatingList = false;
    }

    static Image? LoadImage(string? path)
    {
        if (path == null || !File.Exists(path)) return null;
        try
        {
            using var fs = File.OpenRead(path);
            return new Image { Source = new Bitmap(fs), Stretch = Stretch.UniformToFill };
        }
        catch (Exception) { return null; } // a broken thumbnail is not worth an error
    }

    // --- importing --------------------------------------------------------------------------------------------------

    async Task PickFile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("pick.save"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(L.T("pick.save.filter")) { Patterns = ["*.lsv", "*.rar", "*.zip", "*.7z"] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await ImportFile(path);
    }

    async Task ImportFile(string path)
    {
        if (path.EndsWith(".postac.json", StringComparison.OrdinalIgnoreCase))
        {
            await ImportCharacter(path);
            return;
        }
        var found = await Run(L.F("busy.import", Path.GetFileName(path)), () => Library.Import(path));
        if (found == null) return;
        await RefreshSaves(found.Count == 1 ? found[0] : null);
        if (found.Count > 1) SetFilter("Imported");
        if (found.Count > 1) ShowToast(L.F("toast.found", found.Count));
    }

    async Task PickCharacter()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L.T("pick.char"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(L.T("pick.char.filter")) { Patterns = ["*.postac.json"] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await ImportCharacter(path);
    }

    async Task ImportCharacter(string path)
    {
        if (await Run(L.T("busy.character"), () => Library.ImportCharacter(path)) == null) return;
        if (_current == null)
        {
            ShowToast(L.T("toast.charloaded"));
            return;
        }
        await OpenSave(_current.LsvPath);
        if (!_exports.Any(e => Path.GetFileName(e.Path) == Path.GetFileName(path)))
            ShowToast(L.T("toast.charother"));
    }

    // --- save view --------------------------------------------------------------------------------------------------

    async Task OpenSave(string path)
    {
        var loaded = await Run(L.T("busy.save"), () =>
        {
            var save = Library.Open(path);
            return Tuple.Create(save, Library.FindExports(save));
        });
        if (loaded == null) return;
        (_current, _exports) = loaded;
        ResultPanel.IsVisible = false;
        RenderSave();
        RenderSaveList();
    }

    void CloseSave()
    {
        _current = null;
        _exports = new();
        SaveView.IsVisible = false;
        EmptyState.IsVisible = true;
        ResultPanel.IsVisible = false;
    }

    void RenderSave()
    {
        var s = _current!;
        EmptyState.IsVisible = false;
        SaveView.IsVisible = true;
        var entry = _saves.FirstOrDefault(e => e.Path == s.LsvPath);
        HeroImage.Source = (LoadImage(entry?.Thumb))?.Source;
        HeroFrame.IsVisible = HeroImage.Source != null;
        SaveTitle.Text = s.SaveName;
        SavePath.Text = s.LsvPath;

        var profiles = s.Avatars.Select(a => a.Profile).Distinct().ToList();
        SaveFacts.Children.Clear();
        void Fact(string k, string v)
        {
            var t = new TextBlock { FontSize = 13, Margin = new Thickness(0, 0, 18, 0) };
            t.Inlines!.Add(new Avalonia.Controls.Documents.Run(k + " ") { Foreground = Brush("Muted") });
            t.Inlines.Add(new Avalonia.Controls.Documents.Run(v) { FontWeight = FontWeight.Medium });
            SaveFacts.Children.Add(t);
        }
        Fact(L.T("fact.level"), s.CurrentLevel ?? "?");
        Fact(L.T("fact.version"), s.GameVersion ?? "?");
        Fact(L.T("fact.avatars"), $"{s.Avatars.Count}/4");
        Fact(L.T("fact.players"), profiles.Count.ToString());
        Fact(L.T("fact.ingame"), $"“{s.InGameName}”");

        Cards.Children.Clear();
        foreach (var a in s.Avatars)
            Cards.Children.Add(Card(a, Palette[profiles.IndexOf(a.Profile) % Palette.Length], s.Avatars.Count(o => o.Profile == a.Profile), s.Avatars.Count > 1));

        ParkedCards.Children.Clear();
        foreach (var e in _exports) ParkedCards.Children.Add(ParkedCard(e));
        ParkedEmpty.IsVisible = _exports.Count == 0;
        MainScroll.Offset = new Vector(0, 0);
    }

    Control Card(Avatar a, Color accent, int sameProfile, bool canRemove)
    {
        var body = new StackPanel { Spacing = 0, Margin = new Thickness(18, 18, 18, 18) };

        var initial = new Border
        {
            Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Background = new SolidColorBrush(accent),
            Child = new TextBlock
            {
                Text = a.Name.Trim().FirstOrDefault().ToString().ToUpperInvariant(), FontFamily = (FontFamily)Application.Current!.FindResource("Heading")!,
                FontWeight = FontWeight.Bold, FontSize = 19, Foreground = new SolidColorBrush(Color.Parse("#120F17")),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var title = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = a.Name, Classes = { "h1" }, FontSize = 18, Foreground = Brush("Text"), TextTrimming = TextTrimming.CharacterEllipsis });
        title.Children.Add(new TextBlock { Text = Labels.Race(a.Race), FontSize = 13, Foreground = Brush("Muted") });
        var top = new DockPanel();
        top.Children.Add(initial);
        top.Children.Add(title);
        body.Children.Add(top);

        var stats = new UniformGrid { Columns = 3, Margin = new Thickness(0, 14, 0, 12) };
        void Stat(string k, string v, string? tip = null)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = k.ToUpperInvariant(), FontSize = 10.5, LetterSpacing = 0.6, Foreground = Brush("Muted") });
            sp.Children.Add(new TextBlock { Text = v, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var b = new Border { Classes = { "stat" }, Child = sp, Margin = new Thickness(0, 0, 8, 0) };
            if (tip != null) ToolTip.SetTip(b, tip);
            stats.Children.Add(b);
        }
        Stat(L.T("card.level"), a.Level > 0 ? a.Level.ToString() : "?");
        Stat("XP", a.Xp.ToString());
        Stat(L.T("card.class"), a.Classes.Length > 0 ? Labels.MainClass(a.Classes) : "?", Labels.ClassList(a.Classes));
        body.Children.Add(stats);

        var tags = new WrapPanel { MinHeight = 24 };
        void Tag(string text, string fg, string bg)
        {
            tags.Children.Add(new Border
            {
                Classes = { "tag" }, Background = new SolidColorBrush(Color.Parse(bg)), Margin = new Thickness(0, 0, 6, 6),
                Child = new TextBlock { Text = text, FontSize = 11.5, Foreground = Brush(fg) },
            });
        }
        if (a.IsHost) Tag(L.T("card.host"), "Gold2", "#26D4AF6A");
        if (a.IsMe) Tag(L.T("card.me"), "Ok", "#266CC28F");
        body.Children.Add(tags);

        body.Children.Add(new TextBlock
        {
            Text = a.Recruits.Count > 0 ? L.T("card.recruited") + string.Join(", ", a.Recruits) : " ",
            FontSize = 12.5, Foreground = Brush("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
        });

        var remove = new Button { Content = L.T("card.park"), Classes = { "btn", "danger" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0), IsEnabled = canRemove };
        ToolTip.SetTip(remove, L.T("card.park.tip"));
        remove.Click += async (_, _) => await AskRemove(a);
        body.Children.Add(remove);

        var stack = new DockPanel();
        var bar = new Border { Height = 3, Background = new SolidColorBrush(accent) };
        DockPanel.SetDock(bar, Dock.Top);
        stack.Children.Add(bar);
        stack.Children.Add(body);
        return new Border { Classes = { "card" }, Width = 292, Margin = new Thickness(0, 0, 16, 16), Child = stack };
    }

    Control ParkedCard(ExportFile e)
    {
        var i = e.Info;
        var body = new StackPanel { Margin = new Thickness(16, 14, 16, 16) };
        body.Children.Add(new TextBlock { Text = i.Name, Classes = { "h1" }, FontSize = 17, Foreground = Brush("Text"), TextTrimming = TextTrimming.CharacterEllipsis });
        body.Children.Add(new TextBlock
        {
            Text = $"{Labels.Race(i.Race)} · {Labels.ClassList(i.Classes)}" + (i.Level > 0 ? L.F("parked.lvl", i.Level) : ""),
            FontSize = 12.5, Foreground = Brush("Muted"), TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new TextBlock { Text = L.F("parked.when", i.Exported, i.Save), FontSize = 11.5, Foreground = Brush("Muted"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        if (e.Problem != null)
            body.Children.Add(new TextBlock { Text = e.Problem, FontSize = 12, Foreground = Brush("Danger"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
        var restore = new Button
        {
            Content = L.T("parked.restore"), Classes = { "btn", "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0), IsEnabled = e.Problem == null,
        };
        restore.Click += async (_, _) => await AskRestore(e);
        body.Children.Add(restore);
        return new Border { Classes = { "card" }, Width = 292, Margin = new Thickness(0, 0, 16, 16), Child = body, BorderBrush = new SolidColorBrush(Color.Parse("#40D4AF6A")) };
    }

    // --- actions ----------------------------------------------------------------------------------------------------

    async Task AskRemove(Avatar a)
    {
        var s = _current!;
        var dialog = new RemoveDialog(a, s.Avatars.Where(o => o.Entity != a.Entity).ToList(), s);
        if (await dialog.ShowDialog<bool>(this) != true) return;

        // read the dialog's choices on the UI thread; controls can't be touched from the worker
        var (recruitsTo, toGame, extra, name) = (dialog.RecruitsTo, dialog.ToGame, dialog.ExtraExportPath, dialog.NewName);
        var log = new List<string>();
        var result = await Run(L.F("busy.park", a.Name), () => Library.RemoveAvatar(s.LsvPath, a.Name, recruitsTo, toGame, log.Add, extra, name));
        if (result == null) return;
        ShowResult(result, L.F("done.park", a.Name));
    }

    async Task AskRestore(ExportFile e)
    {
        var s = _current!;
        var dialog = new RestoreDialog(e, s);
        if (await dialog.ShowDialog<bool>(this) != true) return;

        var (profile, takeBack, toGame, name) = (dialog.Profile, dialog.TakeBackRecruits, dialog.ToGame, dialog.NewName);
        var log = new List<string>();
        var result = await Run(L.F("busy.restore", e.Info.Name), () => Library.RestoreAvatar(s.LsvPath, e.Path, profile, takeBack, toGame, log.Add, name));
        if (result == null) return;
        ShowResult(result, L.F("done.restore", e.Info.Name));
    }

    async Task AskRename()
    {
        var s = _current!;
        var dialog = new RenameDialog(s);
        if (await dialog.ShowDialog<bool>(this) != true) return;
        var name = dialog.NewName;
        var log = new List<string>();
        var output = await Run(L.T("busy.rename"), () => Library.RenameSave(s.LsvPath, name, log.Add));
        if (output == null) return;
        await RefreshSaves(output);
        ShowResult(new RemoveResult(output, "", log, _current!.Avatars.Select(a => a.Name).ToList()), L.T("done.rename"));
    }

    async Task AskDelete()
    {
        var s = _current!;
        var dialog = new ConfirmDialog(L.T("delete.title"), L.F("delete.text", s.SaveName, s.LsvPath), L.T("delete.ok"));
        if (await dialog.ShowDialog<bool>(this) != true) return;
        var path = s.LsvPath;
        if (await Run(L.T("busy.delete"), () => { Library.DeleteSave(path); return path; }) == null) return;
        CloseSave();
        await RefreshSaves();
        ShowToast(L.T("toast.deleted"));
    }

    void ShowResult(RemoveResult result, string title)
    {
        _lastResult = result;
        _ = Task.Run(Library.ListSaves).ContinueWith(t => { _saves = t.Result; EnsureVisible(result.Output); RenderSaveList(); }, TaskScheduler.FromCurrentSynchronizationContext());
        ResultTitle.Text = title;
        ResultLog.Text = string.Join("\n", result.Log);
        ResultPanel.IsVisible = true;
        Dispatcher.UIThread.Post(() => ResultPanel.BringIntoView(), DispatcherPriority.Background);
    }

    static void RevealFolder(string dir)
    {
        try
        {
            Process.Start(new ProcessStartInfo(OperatingSystem.IsWindows() ? "explorer.exe" : "xdg-open", $"\"{dir}\"") { UseShellExecute = false });
        }
        catch (Exception) { /* no file manager; the path is in the log */ }
    }

    // --- plumbing ---------------------------------------------------------------------------------------------------

    /// <summary>Run work off the UI thread behind the busy overlay; errors become a toast and a null result.</summary>
    async Task<T?> Run<T>(string label, Func<T> work) where T : class
    {
        BusyLabel.Text = label;
        Busy.IsVisible = true;
        try
        {
            return await Task.Run(work);
        }
        catch (Exception e)
        {
            ShowToast(e is SaveException ? e.Message : $"{e.GetType().Name}: {e.Message}");
            return null;
        }
        finally
        {
            Busy.IsVisible = false;
        }
    }

    void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.IsVisible = true;
        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        _toastTimer.Tick += (_, _) => { Toast.IsVisible = false; _toastTimer.Stop(); };
        _toastTimer.Start();
    }
}
