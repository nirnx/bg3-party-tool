using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Bg3PartyTool.Core;

namespace Bg3PartyTool.Views;

/// <summary>"New save" section shared by the dialogs: its name, whether the game should list it under that name
/// (manual save) and where it goes (straight into the game's saves by default).</summary>
public sealed class SaveTarget : StackPanel
{
    readonly TextBox _name = new() { FontSize = 14 };
    readonly TextBlock _error = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    readonly ComboBox _box = new() { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    readonly TextBlock _hint = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    readonly string? _gameDir = Library.GameSaveDir();

    public bool ToGame => _gameDir != null && _box.SelectedIndex == 0;
    public string NewName => _name.Text?.Trim() ?? "";

    /// <summary>Shows an error under the name box when it isn't a usable file name.</summary>
    public bool Validate()
    {
        var ok = RenameDialog.Valid(NewName);
        _error.Text = L.T("rename.bad");
        _error.IsVisible = !ok;
        return ok;
    }

    public SaveTarget(string defaultName)
    {
        var muted = Ui.Brush("Muted");
        _hint.Foreground = muted;
        _error.Foreground = Ui.Brush("Danger");
        Spacing = 2;

        Children.Add(Small(L.T("target.name")));
        _name.Text = defaultName;
        Children.Add(_name);
        Children.Add(_error);

        Children.Add(Small(L.T("target.where")));
        var items = new List<string>();
        if (_gameDir != null) items.Add(L.T("target.game"));
        items.Add(L.T("target.tool"));
        _box.ItemsSource = items;
        _box.SelectedIndex = 0;
        _box.SelectionChanged += (_, _) => Update();
        Children.Add(_box);
        Children.Add(_hint);
        Update();
    }

    static TextBlock Small(string text) => new() { Text = text, FontSize = 12, Foreground = Ui.Brush("Muted"), Margin = new Thickness(0, 8, 0, 0) };

    void Update() => _hint.Text = ToGame
        ? $"{_gameDir}\n{L.T("target.json")}"
        : _gameDir == null
            ? $"{Library.OutputDir}\n{L.T("target.nogame")}"
            : Library.OutputDir;
}
