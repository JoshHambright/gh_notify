using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using GhNotify.Core;
using ThemeMode = GhNotify.Core.ThemeMode;

namespace GhNotify.UI;

public sealed class GeneralPage : SettingsPageBase
{
    static readonly string[] Days = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
    readonly bool _startWithWindows;
    readonly ThemeMode _themeMode;
    readonly double _textScale;
    readonly bool _loading = true;
    readonly NumberInput _poll, _checkHours, _maxAgents, _timeout;
    string? _captureProblem;

    public GeneralPage(SettingsDraft draft, ISettingsHost host, Action changed) : base(draft, host, changed)
    {
        // POLLING
        Body.Children.Add(Ui.Section("POLLING"));
        _poll = new NumberInput(SettingsPage.General, "pollSeconds", "Poll interval", Config.PollSeconds, n => Config.PollSeconds = n, Changed, 70);
        Body.Children.Add(Ui.Field("Poll interval", Ui.Row(_poll.Box, Muted("seconds")), 22));

        // WORK HOURS
        Body.Children.Add(Ui.Section("WORK HOURS"));
        var chips = new WrapPanel();
        foreach (var day in Days)
        {
            var chip = new ToggleButton { Content = day, MinWidth = 52, IsChecked = Config.WorkHours.Days.Contains(day, StringComparer.OrdinalIgnoreCase) };
            chip.Classes.Add("chip");
            chip.IsCheckedChanged += (_, _) => SetDay(day, chip.IsChecked == true);
            chips.Children.Add(chip);
        }
        Body.Children.Add(Ui.Field("Days", chips, 4));
        Body.Children.Add(Ui.Field("Hours", Ui.Row(
            Ui.Box(Config.WorkHours.Start, "", 64, v => { Config.WorkHours.Start = v; Changed(); }),
            Muted("–"),
            Ui.Box(Config.WorkHours.End, "", 64, v => { Config.WorkHours.End = v; Changed(); })), 22));

        // APP
        Body.Children.Add(Ui.Section("APP"));
        _startWithWindows = SafeStartWithWindows(host);
        StartupCheck = new CheckBox { IsChecked = _startWithWindows, VerticalAlignment = VerticalAlignment.Center };
        StartupCheck.IsCheckedChanged += (_, _) => LocalChoice();
        Body.Children.Add(Ui.Field("Start with Windows", StartupCheck));
        _themeMode = host.ThemeMode;
        ThemeSystem = Ui.Segment("System", _themeMode is not (ThemeMode.Dark or ThemeMode.Light));
        ThemeDark = Ui.Segment("Dark", _themeMode == ThemeMode.Dark);
        ThemeLight = Ui.Segment("Light", _themeMode == ThemeMode.Light);
        foreach (var r in new[] { ThemeSystem, ThemeDark, ThemeLight }) r.IsCheckedChanged += (_, _) => LocalChoice();
        Body.Children.Add(Ui.Field("Theme", Ui.Segments(ThemeSystem, ThemeDark, ThemeLight), 4));
        _textScale = PopupSize.NormalizeScale(host.TextScale);
        TextSizes = PopupSize.TextScales.Select(s => Ui.Segment($"{s * 100:0}%", s == _textScale)).ToList();
        foreach (var r in TextSizes) r.IsCheckedChanged += (_, _) => LocalChoice();
        Body.Children.Add(Ui.Field("Popup text size", Ui.Segments(TextSizes.ToArray()), 4));
        var sizeHint = Ui.Text("Drag the popup's left edge to make it wider; double-click the edge to reset.", "muted", "small");
        sizeHint.Margin = new Thickness(180, 0, 0, 22);
        Body.Children.Add(sizeHint);

        // SHORTCUT
        Body.Children.Add(Ui.Section("SHORTCUT"));
        HotkeyBox = new TextBox { Width = 190, IsReadOnly = true, Cursor = new Cursor(StandardCursorType.Arrow), HorizontalAlignment = HorizontalAlignment.Left };
        HotkeyBox.Classes.Add("pill");
        ToolTip.SetTip(HotkeyBox, "Click, then press the key combination");
        HotkeyBox.AddHandler(InputElement.KeyDownEvent, OnHotkeyKeyDown, RoutingStrategies.Tunnel);
        HotkeyClear = Ui.Btn("Clear", ClearHotkey, "pill");
        Body.Children.Add(Ui.Field("Global hotkey", Ui.Row(HotkeyBox, HotkeyClear), 4));
        var hint = Ui.Text("Click the box and press the keys. Shows or hides the popup from anywhere; empty turns it off.", "muted", "small");
        hint.Margin = new Thickness(180, 0, 0, 0);
        Body.Children.Add(hint);
        HotkeyErrorText = Ui.Text("", "err", "small");
        HotkeyErrorText.Margin = new Thickness(180, 2, 0, 0);
        HotkeyErrorText.IsVisible = false;
        Body.Children.Add(HotkeyErrorText);
        MergeDefault = Ui.Segment("Repo default", Config.Actions.MergeMethod is null);
        MergeSquash = Ui.Segment("Squash", Config.Actions.MergeMethod == MergeMethod.Squash);
        MergeMerge = Ui.Segment("Merge", Config.Actions.MergeMethod == MergeMethod.Merge);
        MergeRebase = Ui.Segment("Rebase", Config.Actions.MergeMethod == MergeMethod.Rebase);
        foreach (var r in new[] { MergeDefault, MergeSquash, MergeMerge, MergeRebase }) r.IsCheckedChanged += (_, _) => MergeChoice();
        var merge = Ui.Field("Default merge method", Ui.Segments(MergeDefault, MergeSquash, MergeMerge, MergeRebase), 22);
        merge.Margin = new Thickness(0, 10, 0, 22);
        Body.Children.Add(merge);
        ShowHotkey();

        // UPDATES
        Body.Children.Add(Ui.Section("UPDATES"));
        Body.Children.Add(Ui.Field("Automatic updates", Ui.Check("Check for updates", Config.Updates.Enabled, v => { Config.Updates.Enabled = v; Changed(); })));
        _checkHours = new NumberInput(SettingsPage.General, "updates.checkHours", "Check every", Config.Updates.CheckHours, n => Config.Updates.CheckHours = n, Changed, 56);
        Body.Children.Add(Ui.Field("Check every", Ui.Row(_checkHours.Box, Muted("hours"))));
        VersionText = Ui.Text("", "field");
        CheckNowButton = Ui.Btn("Check now", () => _ = CheckNowAsync(), "pill");
        CheckNowButton.HorizontalAlignment = HorizontalAlignment.Left;
        var versionRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(VersionText, Dock.Left);
        versionRow.Children.Add(VersionText);
        versionRow.Children.Add(CheckNowButton);
        Body.Children.Add(versionRow);
        UpdateStatusText = Ui.Text("", "muted", "small");
        UpdateStatusText.Margin = new Thickness(180, 0, 0, 22);
        Body.Children.Add(UpdateStatusText);
        ShowUpdateStatus();

        // AGENTS
        Body.Children.Add(Ui.Section("AGENTS"));
        _maxAgents = new NumberInput(SettingsPage.General, "maxConcurrentAgents", "Max concurrent agents", Config.MaxConcurrentAgents, n => Config.MaxConcurrentAgents = n, Changed, 56);
        Body.Children.Add(Ui.Field("Max concurrent agents", _maxAgents.Box));
        _timeout = new NumberInput(SettingsPage.General, "agentTimeoutMinutes", "Agent timeout", Config.AgentTimeoutMinutes, n => Config.AgentTimeoutMinutes = n, Changed, 56);
        Body.Children.Add(Ui.Field("Agent timeout", Ui.Row(_timeout.Box, Muted("minutes"))));

        _loading = false;
    }

    static TextBlock Muted(string text)
    {
        var t = Ui.Text(text, "muted");
        t.VerticalAlignment = VerticalAlignment.Center;
        return t;
    }

    internal CheckBox StartupCheck { get; }
    internal RadioButton ThemeSystem { get; }
    internal RadioButton ThemeDark { get; }
    internal RadioButton ThemeLight { get; }
    /// <summary>One segment per <see cref="PopupSize.TextScales"/> entry, in order.</summary>
    internal IReadOnlyList<RadioButton> TextSizes { get; }
    internal TextBox PollBox => _poll.Box;
    internal TextBox HotkeyBox { get; }
    internal Button HotkeyClear { get; }
    internal TextBlock HotkeyErrorText { get; }
    internal RadioButton MergeDefault { get; }
    internal RadioButton MergeSquash { get; }
    internal RadioButton MergeMerge { get; }
    internal RadioButton MergeRebase { get; }
    internal TextBlock VersionText { get; }
    internal Button CheckNowButton { get; }
    internal TextBlock UpdateStatusText { get; }

    /// <summary>The box shows the draft's hotkey; the in-use line shows only for the combination the app tried to register.</summary>
    void ShowHotkey()
    {
        var text = Config.Hotkey;
        HotkeyBox.Text = string.IsNullOrWhiteSpace(text) ? "Off" : text;
        HotkeyClear.IsEnabled = !string.IsNullOrWhiteSpace(text);
        var inUse = Host.HotkeyError is { } error && string.Equals(text, Host.CurrentConfig.Hotkey, StringComparison.OrdinalIgnoreCase) ? error : null;
        var message = _captureProblem ?? inUse;
        HotkeyErrorText.Text = message ?? "";
        HotkeyErrorText.IsVisible = message is not null;
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
    void OnHotkeyKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Tab or Key.Escape) return;
        e.Handled = true;
        CaptureHotkey(e.Key, e.KeyModifiers);
    }

    /// <summary>One key press in the hotkey box (internal for tests).</summary>
    internal void CaptureHotkey(Key key, KeyModifiers mods)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        if (key is Key.Back or Key.Delete && mods == KeyModifiers.None)
        {
            ClearHotkey();
            return;
        }
        var name = KeyName(key);
        var text = (mods.HasFlag(KeyModifiers.Control) ? "Ctrl+" : "") + (mods.HasFlag(KeyModifiers.Alt) ? "Alt+" : "")
            + (mods.HasFlag(KeyModifiers.Shift) ? "Shift+" : "") + (mods.HasFlag(KeyModifiers.Meta) ? "Win+" : "") + name;
        if (name is null) _captureProblem = "Use a letter, a digit or F1–F12, with Ctrl, Alt or Win.";
        else if (!HotkeyParser.TryParse(text, out var hotkey, out var problem)) _captureProblem = $"Hotkey {problem}.";
        else
        {
            _captureProblem = null;
            Config.Hotkey = hotkey.ToString();
            Changed();
        }
        ShowHotkey();
    }

    internal void ClearHotkey()
    {
        _captureProblem = null;
        Config.Hotkey = "";
        ShowHotkey();
        Changed();
    }

    void MergeChoice()
    {
        if (_loading) return;
        Config.Actions.MergeMethod = MergeSquash.IsChecked == true ? MergeMethod.Squash
            : MergeMerge.IsChecked == true ? MergeMethod.Merge
            : MergeRebase.IsChecked == true ? MergeMethod.Rebase
            : null;
        Changed();
    }

    static bool SafeStartWithWindows(ISettingsHost host)
    {
        try { return host.StartWithWindows; }
        catch (Exception ex) { Log.Error("could not read the startup registration", ex); return false; }
    }

    public bool StartWithWindows => StartupCheck.IsChecked == true;

    public ThemeMode ThemeMode => ThemeDark.IsChecked == true ? ThemeMode.Dark : ThemeLight.IsChecked == true ? ThemeMode.Light : ThemeMode.System;

    public double TextScale => PopupSize.TextScales[Math.Max(0, TextSizes.ToList().FindIndex(r => r.IsChecked == true))];

    /// <summary>Start-with-Windows, theme or text size differ from when the window opened (they are saved outside config.json).</summary>
    public bool HasLocalChanges => StartWithWindows != _startWithWindows || ThemeMode != _themeMode || TextScale != _textScale;

    public void ApplyLocalChoices()
    {
        if (StartWithWindows != _startWithWindows)
        {
            try { Host.StartWithWindows = StartWithWindows; }
            catch (Exception ex) { Log.Error("could not change the startup registration", ex); }
        }
        if (ThemeMode != _themeMode) Host.SetThemeMode(ThemeMode);
        if (TextScale != _textScale) Host.SetTextScale(TextScale);
    }

    void SetDay(string day, bool on)
    {
        var days = Config.WorkHours.Days;
        var set = Days.Where(d => d == day ? on : days.Contains(d, StringComparer.OrdinalIgnoreCase)).ToList();
        days.Clear();
        days.AddRange(set); // always Mon…Sun order
        Changed();
    }

    void LocalChoice()
    {
        if (!_loading) Changed();
    }

    async Task CheckNowAsync()
    {
        CheckNowButton.IsEnabled = false;
        UpdateStatusText.Text = UpdateUi.SettingsStatus(Host.UpdateStatus with { Phase = UpdatePhase.Checking });
        try { await Host.CheckForUpdatesAsync(); }
        catch (Exception ex) { Log.Error("update check from settings failed", ex); }
        ShowUpdateStatus();
    }

    void ShowUpdateStatus()
    {
        var s = Host.UpdateStatus;
        VersionText.Text = UpdateUi.SettingsVersion(s);
        UpdateStatusText.Text = UpdateUi.SettingsStatus(s);
        var (_, enabled, tip) = UpdateUi.CheckItem(s);
        CheckNowButton.IsEnabled = enabled;
        ToolTip.SetTip(CheckNowButton, tip);
    }

    public override IReadOnlyList<SettingsError> InputErrors() => NumberInput.Errors(_poll, _checkHours, _maxAgents, _timeout).ToList();
}
