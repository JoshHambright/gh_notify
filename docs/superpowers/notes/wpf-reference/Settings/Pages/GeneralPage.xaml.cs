using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
                Content = day,
                Style = (Style)FindResource("Style.Chip"),
                MinWidth = 52,
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
        (draft.Config.Actions.MergeMethod switch
        {
            MergeMethod.Squash => MergeSquash,
            MergeMethod.Merge => MergeMerge,
            MergeMethod.Rebase => MergeRebase,
            _ => MergeDefault,
        }).IsChecked = true;
        ShowHotkey();
        _loading = false;
    }

    string? _captureProblem;

    /// <summary>The box shows the draft's hotkey; the in-use line shows only for the combination the app tried to register.</summary>
    void ShowHotkey()
    {
        var text = _draft.Config.Hotkey;
        HotkeyBox.Text = string.IsNullOrWhiteSpace(text) ? "Off" : text;
        HotkeyClear.IsEnabled = !string.IsNullOrWhiteSpace(text);
        var inUse = _host.HotkeyError is { } error && string.Equals(text, _host.CurrentConfig.Hotkey, StringComparison.OrdinalIgnoreCase) ? error : null;
        var message = _captureProblem ?? inUse;
        HotkeyErrorText.Text = message ?? "";
        HotkeyErrorText.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    static string? KeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
        >= Key.F1 and <= Key.F12 => key.ToString(),
        _ => null,
    };

    /// <summary>Records the next key combination while the box has focus; Tab and Esc keep their normal meaning.</summary>
    void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Escape) return;
        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var mods = Keyboard.Modifiers;
        if (key is Key.Back or Key.Delete && mods == ModifierKeys.None)
        {
            OnClearHotkey(sender, e);
            return;
        }
        var name = KeyName(key);
        var text = (mods.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "") + (mods.HasFlag(ModifierKeys.Alt) ? "Alt+" : "")
            + (mods.HasFlag(ModifierKeys.Shift) ? "Shift+" : "") + (mods.HasFlag(ModifierKeys.Windows) ? "Win+" : "") + name;
        if (name is null) _captureProblem = "Use a letter, a digit or F1–F12, with Ctrl, Alt or Win.";
        else if (!HotkeyParser.TryParse(text, out var hotkey, out var problem)) _captureProblem = $"Hotkey {problem}.";
        else
        {
            _captureProblem = null;
            _draft.Config.Hotkey = hotkey.ToString();
            _changed();
        }
        ShowHotkey();
    }

    void OnClearHotkey(object sender, RoutedEventArgs e)
    {
        _captureProblem = null;
        _draft.Config.Hotkey = "";
        ShowHotkey();
        _changed();
    }

    void OnMergeChoice(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _draft.Config.Actions.MergeMethod = MergeSquash.IsChecked == true ? MergeMethod.Squash
            : MergeMerge.IsChecked == true ? MergeMethod.Merge
            : MergeRebase.IsChecked == true ? MergeMethod.Rebase
            : null;
        _changed();
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
