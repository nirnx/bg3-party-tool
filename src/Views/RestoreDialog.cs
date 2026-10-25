using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Bg3PartyTool.Core;

namespace Bg3PartyTool.Views;

/// <summary>Restoring a parked character: which player gets it, whether its companions come back, where to save.</summary>
public sealed class RestoreDialog : Window
{
    readonly ComboBox _owner = new();
    readonly SaveTarget _target;
    readonly CheckBox _recruits = new();
    readonly List<string> _profiles = new();

    public string Profile => _profiles[Math.Max(0, _owner.SelectedIndex)];
    public bool TakeBackRecruits => _recruits.IsVisible && _recruits.IsChecked == true;
    public bool ToGame => _target.ToGame;
    public string NewName => _target.NewName;

    public RestoreDialog(ExportFile export, SaveFile save)
    {
        var info = export.Info;
        _target = new SaveTarget(Library.DefaultName(save.SaveName, "name.with", info.Name));
        Title = L.F("restore.title", info.Name);
        Width = 540;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.Brush("Panel");

        var root = new StackPanel { Margin = new Thickness(24), Spacing = 6 };
        root.Children.Add(new TextBlock { Text = L.F("restore.title", info.Name), Classes = { "h1" }, FontSize = 21 });
        root.Children.Add(new TextBlock
        {
            Text = L.F("restore.intro", info.Name, Labels.Race(info.Race), Labels.ClassList(info.Classes), save.SaveName),
            Foreground = Ui.Brush("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
        });

        // owners: players already in the save, plus the original owner if they are not
        root.Children.Add(Ui.Label(L.T("restore.owner")));
        var labels = new List<string>();
        foreach (var group in save.Avatars.GroupBy(a => a.Profile))
        {
            var tags = new List<string>();
            if (group.Any(a => a.IsHost)) tags.Add(L.T("restore.tag.host"));
            if (group.Any(a => a.IsMe)) tags.Add(L.T("restore.tag.me"));
            if (group.Key == info.Profile) tags.Add(L.T("restore.tag.orig"));
            _profiles.Add(group.Key);
            labels.Add(L.F("restore.player", string.Join(", ", group.Select(a => a.Name))) + (tags.Count > 0 ? $" ({string.Join(", ", tags)})" : ""));
        }
        if (!_profiles.Contains(info.Profile))
        {
            _profiles.Insert(0, info.Profile);
            var me = Library.LocalProfile() == info.Profile ? L.T("restore.orig.me") : "";
            labels.Insert(0, L.F("restore.orig", me));
        }
        _owner.ItemsSource = labels;
        _owner.SelectedIndex = _profiles.IndexOf(info.Profile);
        root.Children.Add(_owner);
        root.Children.Add(new TextBlock
        {
            Text = L.T("restore.note"), FontSize = 12, Foreground = Ui.Brush("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        });

        var recruits = save.RecruitsToTakeBack(System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(export.Path))!.AsObject());
        if (recruits.Count > 0)
        {
            _recruits.IsChecked = true;
            _recruits.Content = new TextBlock { Text = L.F("restore.recruits", string.Join(", ", recruits)), TextWrapping = TextWrapping.Wrap };
            _recruits.Margin = new Thickness(0, 10, 0, 0);
            root.Children.Add(_recruits);
        }
        else _recruits.IsVisible = false;

        root.Children.Add(Ui.Label(L.T("target.label")));
        root.Children.Add(_target);

        var cancel = new Button { Content = L.T("cancel"), Classes = { "btn" }, Margin = new Thickness(0, 0, 10, 0) };
        var ok = new Button { Content = L.T("restore.ok"), Classes = { "btn", "primary" } };
        cancel.Click += (_, _) => Close(false);
        ok.Click += (_, _) => { if (_target.Validate()) Close(true); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;
    }
}
