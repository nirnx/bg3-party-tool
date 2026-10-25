using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Bg3PartyTool.Core;

namespace Bg3PartyTool.Views;

/// <summary>Confirmation for parking an avatar: who takes over its companions and where the new save goes.</summary>
public sealed class RemoveDialog : Window
{
    readonly ComboBox _recruits = new();
    readonly SaveTarget _target;
    readonly TextBlock _fileHint = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    readonly List<Avatar> _others;
    string? _extraExport;

    public string? RecruitsTo => _recruits.IsVisible && _recruits.SelectedIndex >= 0 ? _others[_recruits.SelectedIndex].Name : null;
    public bool ToGame => _target.ToGame;
    /// <summary>Extra location for the .postac.json chosen by the user, or null for the tool's folder only.</summary>
    public string? ExtraExportPath => _extraExport;

    public string NewName => _target.NewName;

    public RemoveDialog(Avatar a, List<Avatar> others, SaveFile save)
    {
        _others = others;
        var saveName = save.SaveName;
        _target = new SaveTarget(Library.DefaultName(save.SaveName, "name.without", a.Name));
        Title = L.F("park.title", a.Name);
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.Brush("Panel");

        var root = new StackPanel { Margin = new Thickness(24), Spacing = 6 };
        root.Children.Add(new TextBlock { Text = L.F("park.title", a.Name), Classes = { "h1" }, FontSize = 21 });
        root.Children.Add(new TextBlock
        {
            Text = L.F("park.intro", a.Name, Labels.Race(a.Race), Labels.ClassList(a.Classes), saveName),
            Foreground = Ui.Brush("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 4),
        });
        foreach (var line in new[] { L.T("park.b1"), L.T("park.b2"), L.T("park.b3") })
        {
            var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Ui.Brush("Muted"), FontSize = 13 };
            t.Inlines!.Add(new Run("•  ") { Foreground = Ui.Brush("Gold") });
            t.Inlines.Add(new Run(line));
            root.Children.Add(t);
        }

        if (a.Recruits.Count > 0)
        {
            root.Children.Add(Ui.Label(L.F("park.recruits", string.Join(", ", a.Recruits))));
            _recruits.ItemsSource = others.Select(o => o.IsHost ? L.F("park.host", o.Name) : o.Name).ToList();
            _recruits.SelectedIndex = Math.Max(0, others.FindIndex(o => o.IsHost));
            root.Children.Add(_recruits);
        }
        else _recruits.IsVisible = false;

        root.Children.Add(Ui.Label(L.T("target.label")));
        root.Children.Add(_target);

        root.Children.Add(Ui.Label(L.T("park.file")));
        _fileHint.Foreground = Ui.Brush("Muted");
        _fileHint.Text = L.T("park.file.default");
        var pick = new Button { Content = L.T("park.file.pick"), Classes = { "btn" }, Margin = new Thickness(0, 6, 0, 0) };
        pick.Click += async (_, _) =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = L.T("park.file.title"), SuggestedFileName = $"{Library.SafeName(a.Name)}.postac.json", DefaultExtension = "json",
                FileTypeChoices = [new Avalonia.Platform.Storage.FilePickerFileType("BG3 Party Tool") { Patterns = ["*.postac.json"] }],
            });
            if (file?.TryGetLocalPath() is { } path)
            {
                if (!path.EndsWith(".postac.json", StringComparison.OrdinalIgnoreCase)) path = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? path[..^5] + ".postac.json" : path + ".postac.json";
                _extraExport = path;
                _fileHint.Text = L.T("park.file.default") + "\n" + L.F("park.file.extra", path);
            }
        };
        root.Children.Add(_fileHint);
        root.Children.Add(pick);

        var cancel = new Button { Content = L.T("cancel"), Classes = { "btn" }, Margin = new Thickness(0, 0, 10, 0) };
        var ok = new Button { Content = L.T("park.ok"), Classes = { "btn", "danger" } };
        cancel.Click += (_, _) => Close(false);
        ok.Click += (_, _) => { if (_target.Validate()) Close(true); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;
    }
}

/// <summary>Small helpers shared by the dialogs.</summary>
static class Ui
{
    public static IBrush Brush(string key) => (IBrush)Application.Current!.FindResource(key)!;

    public static TextBlock Label(string text) => new()
    {
        Text = text.ToUpperInvariant(), FontSize = 11.5, LetterSpacing = 0.6, Foreground = Brush("Muted"), Margin = new Thickness(0, 14, 0, 2),
        TextWrapping = TextWrapping.Wrap,
    };
}
