using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Bg3PartyTool.Core;

namespace Bg3PartyTool.Views;

/// <summary>Rename a save: new folder/file name, with a note on what the game's Load menu will show.</summary>
public sealed class RenameDialog : Window
{
    readonly TextBox _name = new();
    readonly TextBlock _error = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false };

    public string NewName => _name.Text?.Trim() ?? "";

    static readonly char[] Forbidden = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];
    public static bool Valid(string name) => name.Length > 0 && name.IndexOfAny(Forbidden) < 0 && name.Trim('.', ' ').Length > 0;

    public RenameDialog(SaveFile save)
    {
        Title = L.T("rename.title");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.Brush("Panel");
        _error.Foreground = Ui.Brush("Danger");

        var root = new StackPanel { Margin = new Thickness(24), Spacing = 6 };
        root.Children.Add(new TextBlock { Text = L.T("rename.title"), Classes = { "h1" }, FontSize = 21 });

        root.Children.Add(Ui.Label(L.T("rename.name")));
        _name.Text = save.SaveName;
        _name.FontSize = 15;
        root.Children.Add(_name);
        root.Children.Add(_error);
        root.Children.Add(new TextBlock
        {
            Text = L.F("rename.info", save.FolderPrefix), FontSize = 12, Foreground = Ui.Brush("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
        });

        var typeName = save.SaveType switch { 1 => L.T("type.quick"), 2 => L.T("type.auto"), _ => L.T("type.manual") };
        var auto = save.SaveType is 1 or 2;
        root.Children.Add(new TextBlock
        {
            Text = auto ? L.F("rename.ingame.auto", save.InGameName, typeName) + " " + L.T("rename.becomes") : L.T("rename.ingame.manual"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
        });

        var cancel = new Button { Content = L.T("cancel"), Classes = { "btn" }, Margin = new Thickness(0, 0, 10, 0) };
        var ok = new Button { Content = L.T("rename.ok"), Classes = { "btn", "primary" } };
        cancel.Click += (_, _) => Close(false);
        ok.Click += (_, _) =>
        {
            if (!Valid(NewName)) { _error.Text = L.T("rename.bad"); _error.IsVisible = true; return; }
            Close(true);
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;
        Opened += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }
}

/// <summary>Yes/no question with a danger-styled confirm button.</summary>
public sealed class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string text, string okText)
    {
        Title = title;
        Width = 500;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.Brush("Panel");

        var root = new StackPanel { Margin = new Thickness(24), Spacing = 6 };
        root.Children.Add(new TextBlock { Text = title, Classes = { "h1" }, FontSize = 21 });
        root.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        var cancel = new Button { Content = L.T("cancel"), Classes = { "btn" }, Margin = new Thickness(0, 0, 10, 0) };
        var ok = new Button { Content = okText, Classes = { "btn", "danger" } };
        cancel.Click += (_, _) => Close(false);
        ok.Click += (_, _) => Close(true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;
    }
}
