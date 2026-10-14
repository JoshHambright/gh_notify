using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GhNotify.Core;

namespace GhNotify.App;

public partial class NotificationsPage : UserControl, ISettingsPage
{
    readonly SettingsDraft _draft;
    readonly Action _changed;

    public NotificationsPage(SettingsDraft draft, ISettingsHost host, Action changed)
    {
        InitializeComponent();
        _draft = draft;
        _changed = changed;
        ShowRules();
    }

    List<SoundRule> Sounds => _draft.Config.Notifications.Sounds;

    void ShowRules()
    {
        var rows = Sounds.Select(r => new SoundRow(r, _changed, PickWav)).ToList();
        Rules.ItemsSource = rows;
        Summaries.ItemsSource = rows;
    }

    /// <summary>"Custom file…": a .wav picker; the list is rebuilt afterwards so the combo shows the picked file (or the old sound).</summary>
    string? PickWav()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose a sound", Filter = "Wave sounds (*.wav)|*.wav" };
        var path = dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
        Dispatcher.InvokeAsync(ShowRules, DispatcherPriority.Background);
        return path;
    }

    void OnPlay(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is SoundRow row && row.Rule.Sound != "none") SoundPlayerService.PlayOrDefault(row.Rule.Sound);
    }

    void OnAdd(object sender, RoutedEventArgs e)
    {
        Sounds.Add(new SoundRule());
        ShowRules();
        _changed();
    }

    void OnUp(object sender, RoutedEventArgs e) => Move(sender, -1);
    void OnDown(object sender, RoutedEventArgs e) => Move(sender, 1);

    void Move(object sender, int delta)
    {
        if (((FrameworkElement)sender).Tag is not SoundRow row || !SettingsDraft.Move(Sounds, Sounds.IndexOf(row.Rule), delta)) return;
        ShowRules();
        _changed();
    }

    void OnRemove(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not SoundRow row) return;
        Sounds.Remove(row.Rule);
        ShowRules();
        _changed();
    }

    public void ShowErrors(IReadOnlyList<SettingsError> errors) => ErrorList.ItemsSource = errors.Select(e => e.Message).ToList();
}
