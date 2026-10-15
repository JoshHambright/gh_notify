using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GhNotify.Core;

namespace GhNotify.UI;

/// <summary>Shared base of the six pages: an error list on top, then the page body. Pages build their controls in code and edit the draft directly.</summary>
public abstract class SettingsPageBase : UserControl, ISettingsPage
{
    readonly StackPanel _errors = new() { Margin = new Thickness(0, 0, 0, 8) };

    protected SettingsPageBase(SettingsDraft draft, ISettingsHost host, Action changed)
    {
        Draft = draft;
        Host = host;
        Changed = changed;
        Body = new StackPanel();
        Body.Children.Add(_errors);
        Content = Body;
    }

    protected SettingsDraft Draft { get; }
    protected ISettingsHost Host { get; }
    protected Action Changed { get; }
    protected StackPanel Body { get; }
    protected AppConfig Config => Draft.Config;

    /// <summary>The validation messages shown at the top of the page.</summary>
    public IReadOnlyList<string> ShownErrors { get; private set; } = [];

    public void ShowErrors(IReadOnlyList<SettingsError> errors)
    {
        ShownErrors = errors.Select(e => e.Message).ToList();
        _errors.Children.Clear();
        foreach (var message in ShownErrors)
            _errors.Children.Add(Ui.Text(message, "err", "small"));
    }

    public virtual IReadOnlyList<SettingsError> InputErrors() => [];
}

/// <summary>Small control builders so the pages read like the layout they produce.</summary>
static class Ui
{
    public static TextBlock Text(string text, params string[] classes)
    {
        var t = new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        foreach (var c in classes) t.Classes.Add(c);
        return t;
    }

    public static Control Section(string title, double bottom = 12)
    {
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, bottom), Spacing = 8 };
        var bars = new Avalonia.Controls.Shapes.Path();
        bars.Classes.Add("ebars");
        heading.Children.Add(bars);
        heading.Children.Add(Text(title, "section"));
        return heading;
    }

    /// <summary>A "label | control" row; the label is the fixed 180 px left column.</summary>
    public static DockPanel Field(string label, Control content, double bottom = 10, VerticalAlignment labelAlign = VerticalAlignment.Center)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, bottom) };
        var l = Text(label, "field");
        l.VerticalAlignment = labelAlign;
        if (labelAlign == VerticalAlignment.Top) l.Margin = new Thickness(0, 4, 0, 0);
        DockPanel.SetDock(l, Dock.Left);
        row.Children.Add(l);
        row.Children.Add(content);
        return row;
    }

    public static Control Row(params Control[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        p.Children.AddRange(children);
        return p;
    }

    public static TextBox Box(string? text, string watermark, double width, Action<string> onText, bool stretch = false)
    {
        var box = new TextBox { Text = text ?? "", Watermark = watermark, HorizontalAlignment = HorizontalAlignment.Left };
        if (!stretch) box.Width = width;
        else box.HorizontalAlignment = HorizontalAlignment.Stretch;
        box.Classes.Add("pill");
        box.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) onText(box.Text ?? ""); };
        return box;
    }

    public static CheckBox Check(string content, bool value, Action<bool> set)
    {
        var box = new CheckBox { Content = content, IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
        box.IsCheckedChanged += (_, _) => set(box.IsChecked == true);
        return box;
    }

    public static ComboBox Combo(IEnumerable<string> items, int selected, Action<int> set, double minWidth)
    {
        var combo = new ComboBox { ItemsSource = items.ToList(), MinWidth = minWidth, SelectedIndex = selected };
        combo.Classes.Add("pill");
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) set(combo.SelectedIndex); };
        return combo;
    }

    public static Button Btn(string content, Action click, params string[] classes)
    {
        var b = new Button { Content = content };
        foreach (var c in classes) b.Classes.Add(c);
        b.Click += (_, _) => click();
        return b;
    }

    public static Button Remove(Action click, string tip = "Remove", bool tiny = false)
    {
        var b = Btn("✕", click, tiny ? ["icon", "tiny"] : ["icon"]);
        if (!tiny) { b.FontSize = 11; b.Width = 22; b.Height = 22; }
        ToolTip.SetTip(b, tip);
        return b;
    }

    public static Border Segments(params Control[] radios)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.AddRange(radios);
        var border = new Border { Child = panel };
        border.Classes.Add("segments");
        return border;
    }

    public static RadioButton Segment(string content, bool isChecked = false)
    {
        var r = new RadioButton { Content = content, IsChecked = isChecked };
        r.Classes.Add("segment");
        return r;
    }

    public static Border Card(Control child)
    {
        var b = new Border { Child = child };
        b.Classes.Add("card");
        return b;
    }

    /// <summary>The control's foreground follows a theme token, so it changes with light / dark.</summary>
    public static void TokenForeground(TextBlock t, string key) =>
        t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable(key));

    // ----- Pickers (Avalonia StorageProvider; cancelling returns null) -----

    public static async Task<string?> PickFile(Visual from, string title, string filterName, params string[] patterns)
    {
        if (TopLevel.GetTopLevel(from)?.StorageProvider is not { CanOpen: true } storage) return null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(filterName) { Patterns = patterns }],
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public static async Task<string?> PickFolder(Visual from, string title, string? startAt)
    {
        if (TopLevel.GetTopLevel(from)?.StorageProvider is not { CanPickFolder: true } storage) return null;
        IStorageFolder? start = Directory.Exists(startAt) ? await storage.TryGetFolderFromPathAsync(startAt) : null;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false, SuggestedStartLocation = start });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    public static void Post(Action action) => Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
}
