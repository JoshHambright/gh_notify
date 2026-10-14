using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GhNotify.Core;

namespace GhNotify.App;

public partial class FiltersPage : UserControl, ISettingsPage
{
    readonly SettingsDraft _draft;
    readonly ISettingsHost _host;
    readonly Action _changed;
    readonly IReadOnlyList<InboxItem> _items;

    public FiltersPage(SettingsDraft draft, ISettingsHost host, Action changed)
    {
        InitializeComponent();
        _draft = draft;
        _host = host;
        _changed = changed;
        _items = host.CurrentItems();
        ShowChips();
        ShowTimedMutes();
    }

    FiltersConfig Filters => _draft.Config.Filters;

    void ShowChips()
    {
        MuteChips.ItemsSource = Filters.Mute.ToList();
        HideChips.ItemsSource = Filters.Hide.ToList();
    }

    (List<string> List, TextBox Box, TextBlock Hint) Target(object sender) =>
        sender == HideBox || FindChips(sender)?.Tag as string == "hide" ? (Filters.Hide, HideBox, HideHint) : (Filters.Mute, MuteBox, MuteHint);

    static ItemsControl? FindChips(object sender)
    {
        for (var d = sender as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is ItemsControl { Tag: string } chips) return chips;
        return null;
    }

    void OnPatternKey(object sender, KeyEventArgs e)
    {
        var (list, box, _) = Target(sender);
        if (e.Key != Key.Enter || SettingsOptions.Blank(box.Text) is not { } pattern) return;
        e.Handled = true;
        if (!list.Contains(pattern, StringComparer.OrdinalIgnoreCase)) list.Add(pattern);
        box.Clear();
        ShowChips();
        _changed();
    }

    /// <summary>Live hint under the add box: how many current items the pattern matches, or that it is invalid.</summary>
    void OnPatternTyped(object sender, TextChangedEventArgs e)
    {
        var (_, box, hint) = Target(sender);
        if (string.IsNullOrWhiteSpace(box.Text))
        {
            hint.Text = "";
            return;
        }
        var n = SettingsDraft.CountMatches(box.Text, _items);
        hint.Text = n < 0 ? "invalid pattern" : $"matches {n} current item{(n == 1 ? "" : "s")}";
        hint.SetResourceReference(TextBlock.ForegroundProperty, n < 0 ? "Brush.Red" : "Brush.Muted");
    }

    void OnRemovePattern(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not string pattern) return;
        Target(sender).List.Remove(pattern);
        ShowChips();
        _changed();
    }

    // ----- Timed mutes (app state, not config: Unmute applies immediately) -----

    void ShowTimedMutes()
    {
        var timed = _host.ActiveMutes().Where(m => !m.Permanent).ToList();
        TimedMutes.ItemsSource = timed;
        NoTimedMutes.Visibility = timed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnUnmute(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not string pattern) return;
        _host.Unmute(pattern);
        ShowTimedMutes();
    }

    public void ShowErrors(IReadOnlyList<SettingsError> errors) => ErrorList.ItemsSource = errors.Select(e => e.Message).ToList();
}
