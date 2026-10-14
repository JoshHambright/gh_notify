using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GhNotify.Core;
using ThemeMode = GhNotify.Core.ThemeMode;

namespace GhNotify.App;

public partial class GeneralPage : UserControl, ISettingsPage
{
    static readonly string[] Days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
    readonly SettingsDraft _draft;
    readonly ISettingsHost _host;
    readonly Action _changed;
    readonly bool _startWithWindows;
    readonly ThemeMode _themeMode;
    readonly bool _loading = true;

    public GeneralPage(SettingsDraft draft, ISettingsHost host, Action changed)
    {
        InitializeComponent();
        _draft = draft;
        _host = host;
        _changed = changed;
        DataContext = draft.Config;

        foreach (var day in Days)
        {
            var chip = new ToggleButton
            {
                Content = day, Style = (Style)FindResource("Style.Chip"), MinWidth = 52,
                IsChecked = draft.Config.WorkHours.Days.Contains(day, StringComparer.OrdinalIgnoreCase),
            };
            chip.Checked += (_, _) => SetDay(day, true);
            chip.Unchecked += (_, _) => SetDay(day, false);
            DayChips.Children.Add(chip);
        }

        _startWithWindows = SafeStartWithWindows(host);
        StartupCheck.IsChecked = _startWithWindows;
        _themeMode = host.ThemeMode;
        (_themeMode switch { ThemeMode.Dark => ThemeDark, ThemeMode.Light => ThemeLight, _ => ThemeSystem }).IsChecked = true;
        ShowUpdateStatus();
        _loading = false;
    }

    static bool SafeStartWithWindows(ISettingsHost host)
    {
        try { return host.StartWithWindows; }
        catch (Exception ex) { Log.Error("could not read the startup registration", ex); return false; }
    }

    public bool StartWithWindows => StartupCheck.IsChecked == true;

    public ThemeMode ThemeMode => ThemeDark.IsChecked == true ? ThemeMode.Dark : ThemeLight.IsChecked == true ? ThemeMode.Light : ThemeMode.System;

    /// <summary>Start-with-Windows or theme differ from when the window opened (they are saved outside config.json).</summary>
    public bool HasLocalChanges => StartWithWindows != _startWithWindows || ThemeMode != _themeMode;

    public void ApplyLocalChoices()
    {
        if (StartWithWindows != _startWithWindows)
        {
            try { _host.StartWithWindows = StartWithWindows; }
            catch (Exception ex) { Log.Error("could not change the startup registration", ex); }
        }
        if (ThemeMode != _themeMode) _host.SetThemeMode(ThemeMode);
    }

    void SetDay(string day, bool on)
    {
        var days = _draft.Config.WorkHours.Days;
        var set = Days.Where(d => d == day ? on : days.Contains(d, StringComparer.OrdinalIgnoreCase)).ToList();
        days.Clear();
        days.AddRange(set); // always Mon…Sun order
        _changed();
    }

    void OnEdited(object sender, RoutedEventArgs e)
    {
        if (!_loading) _changed();
    }

    void OnLocalChoice(object sender, RoutedEventArgs e)
    {
        if (!_loading) _changed();
    }

    async void OnCheckNow(object sender, RoutedEventArgs e)
    {
        CheckNowButton.IsEnabled = false;
        UpdateStatusText.Text = UpdateUi.SettingsStatus(_host.UpdateStatus with { Phase = UpdatePhase.Checking });
        try { await _host.CheckForUpdatesAsync(); }
        catch (Exception ex) { Log.Error("update check from settings failed", ex); }
        ShowUpdateStatus();
    }

    void ShowUpdateStatus()
    {
        var s = _host.UpdateStatus;
        VersionText.Text = UpdateUi.SettingsVersion(s);
        UpdateStatusText.Text = UpdateUi.SettingsStatus(s);
        var (_, enabled, tip) = UpdateUi.CheckItem(s);
        CheckNowButton.IsEnabled = enabled;
        CheckNowButton.ToolTip = tip;
    }

    public IReadOnlyList<SettingsError> InputErrors() => NumberInput.Errors(SettingsPage.General,
        (PollBox, "pollSeconds", "Poll interval"), (CheckHoursBox, "updates.checkHours", "Check every"),
        (MaxAgentsBox, "maxConcurrentAgents", "Max concurrent agents"), (TimeoutBox, "agentTimeoutMinutes", "Agent timeout")).ToList();

    public void ShowErrors(IReadOnlyList<SettingsError> errors) => ErrorList.ItemsSource = errors.Select(e => e.Message).ToList();
}
