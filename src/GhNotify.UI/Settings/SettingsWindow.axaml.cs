using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GhNotify.Core;

namespace GhNotify.UI;

/// <summary>
/// Visual editor for config.json. Edits go to a <see cref="SettingsDraft"/>; nothing touches disk until Save.
/// No modal dialogs: discard, conflict and comment warnings are inline bars in the bottom bar.
/// The constructor takes the live host (AppController, or SampleSettingsHost in tests).
/// </summary>
public partial class SettingsWindow : Window
{
    readonly ISettingsHost _host;
    readonly DispatcherTimer _debounce;
    readonly Dictionary<SettingsPage, Control> _pages = new();
    readonly Dictionary<SettingsPage, (RadioButton Nav, Control Dot)> _nav;
    IReadOnlyList<SettingsError> _errors = [];
    SettingsPage _page = SettingsPage.General;
    GeneralPage _general = null!;
    bool _closeConfirmed;
    bool _commentsAccepted;

    public SettingsWindow() : this(new SampleSettingsHost()) { }

    public SettingsWindow(ISettingsHost host)
    {
        InitializeComponent();
        _host = host;
        _nav = new()
        {
            [SettingsPage.General] = (NavGeneral, DotGeneral),
            [SettingsPage.Sources] = (NavSources, DotSources),
            [SettingsPage.Notifications] = (NavNotifications, DotNotifications),
            [SettingsPage.Filters] = (NavFilters, DotFilters),
            [SettingsPage.Workflows] = (NavWorkflows, DotWorkflows),
            [SettingsPage.Advanced] = (NavAdvanced, DotAdvanced),
        };
        foreach (var (page, (nav, _)) in _nav)
        {
            var target = page;
            nav.IsCheckedChanged += (_, _) => { if (nav.IsChecked == true && target != _page) ShowPage(target); };
        }
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounce.Tick += (_, _) => Revalidate();
        Draft = new SettingsDraft(host.CurrentConfig, host.ConfigFileHash());
        BuildPages();
        ShowConfigError();
        ShowPage(SettingsPage.General);
        Revalidate();
    }

    public ISettingsHost Host => _host;
    public SettingsDraft Draft { get; private set; }

    /// <summary>Something to save (config edits, or the General page's start-with-Windows / theme choices) and no errors.</summary>
    public bool CanSave => (Draft.IsDirty || _general.HasLocalChanges) && _errors.Count == 0;

    /// <summary>The page shown now; internal for tests and previews.</summary>
    internal Control CurrentPage => _pages[_page];
    internal Control Page(SettingsPage page) => _pages[page];
    internal string StatusMessage => StatusText.Text ?? "";
    internal bool DiscardBarShown => DiscardBar.IsVisible;
    internal bool ConflictBarShown => ConflictBar.IsVisible;
    internal bool CommentsBarShown => CommentsBar.IsVisible;
    internal bool ConfigErrorShown => ConfigErrorBar.IsVisible;
    internal bool NavDotShown(SettingsPage page) => _nav[page].Dot.IsVisible;
    internal bool SaveEnabled => SaveButton.IsEnabled;

    void BuildPages()
    {
        _pages.Clear();
        _general = new GeneralPage(Draft, _host, Changed);
        _pages[SettingsPage.General] = _general;
        _pages[SettingsPage.Sources] = new SourcesPage(Draft, _host, Changed);
        _pages[SettingsPage.Notifications] = new NotificationsPage(Draft, _host, Changed);
        _pages[SettingsPage.Filters] = new FiltersPage(Draft, _host, Changed);
        _pages[SettingsPage.Workflows] = new WorkflowsPage(Draft, _host, Changed);
        var advanced = new AdvancedPage(Draft, _host, Changed);
        advanced.ResetConfirmed += ResetToDefaults;
        _pages[SettingsPage.Advanced] = advanced;
        PageHost.Content = _pages[_page];
    }

    public void ShowPage(SettingsPage page)
    {
        _page = page;
        PageHost.Content = _pages[page];
        PageScroller.ScrollToHome();
        _nav[page].Nav.IsChecked = true;
    }

    /// <summary>Scrolls the page to its end (previews).</summary>
    internal void ScrollToEnd() => PageScroller.ScrollToEnd();

    /// <summary>Called by the pages after every edit; validation runs 250 ms after the last one.</summary>
    void Changed()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Validates now (no debounce): nav dots, status text, Save's enabled state and each page's error list.</summary>
    public void Revalidate()
    {
        _debounce.Stop();
        // Draft rules, plus text the pages could not turn into a value (e.g. "abc" in a number box).
        _errors = [.. Draft.Validate(), .. _pages.Values.SelectMany(p => ((ISettingsPage)p).InputErrors())];
        foreach (var (page, (_, dot)) in _nav)
            dot.IsVisible = _errors.Any(e => e.Page == page);
        foreach (var (page, element) in _pages)
            ((ISettingsPage)element).ShowErrors(_errors.Where(e => e.Page == page).ToList());
        SaveButton.IsEnabled = CanSave;
        StatusText.Text = Status();
    }

    string Status()
    {
        if (_errors.Count > 0)
        {
            var pages = _errors.Select(e => e.Page).Distinct().Select(p => p.ToString());
            return $"{_errors.Count} problem{(_errors.Count == 1 ? "" : "s")} on {string.Join(", ", pages)}";
        }
        return Draft.IsDirty || _general.HasLocalChanges ? "Unsaved changes" : "";
    }

    /// <summary>The top bar shown while config.json on disk is broken: the window edits the last working settings.</summary>
    void ShowConfigError()
    {
        var error = _host.ConfigError;
        ConfigErrorBar.IsVisible = error is not null;
        if (error is null) return;
        const string prefix = "config.json: ";
        if (error.StartsWith(prefix, StringComparison.Ordinal)) error = error[prefix.Length..];
        ConfigErrorText.Text = $"config.json has an error: {error} — you're editing the last working settings; Save will ask before replacing the file.";
    }

    /// <summary>Shows one inline bar (or none) in place of the status text.</summary>
    void ShowBar(Control? bar)
    {
        foreach (var b in new Control[] { DiscardBar, ConflictBar, CommentsBar })
            b.IsVisible = b == bar;
        StatusText.IsVisible = bar is null;
    }

    void OnSave(object? sender, RoutedEventArgs e) => Save();

    internal void Save()
    {
        Revalidate();
        if (!CanSave) return;
        ShowBar(null);
        if (Draft.IsDirty)
        {
            if (!_commentsAccepted && !_host.CommentsWarningShown && ConfigHasComments())
            {
                ShowBar(CommentsBar);
                return;
            }
            // ConfigStore treats a null hash as "overwrite": a file that appeared since the window opened is a conflict too.
            if (Draft.FileHashAtOpen is null && File.Exists(_host.ConfigPath))
            {
                ShowBar(ConflictBar);
                return;
            }
            if (!TrySave(Draft.FileHashAtOpen)) return;
        }
        Finish();
    }

    bool ConfigHasComments()
    {
        try { return File.Exists(_host.ConfigPath) && SettingsDraft.HasComments(File.ReadAllText(_host.ConfigPath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    bool TrySave(string? expectedHash)
    {
        try
        {
            _host.SaveConfig(Draft.Config, expectedHash);
            return true;
        }
        catch (ConfigConflictException)
        {
            ShowBar(ConflictBar);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            ShowBar(null);
            StatusText.Text = ex.Message;
        }
        return false;
    }

    /// <summary>Applies the General page's choices that live outside config.json, then closes.</summary>
    void Finish()
    {
        _general.ApplyLocalChoices();
        _closeConfirmed = true;
        Close();
    }

    void OnSaveAnyway(object? sender, RoutedEventArgs e) => SaveAnyway();

    internal void SaveAnyway()
    {
        _host.CommentsWarningShown = true;
        _commentsAccepted = true;
        Save();
    }

    void OnOverwrite(object? sender, RoutedEventArgs e) => Overwrite();

    internal void Overwrite()
    {
        ShowBar(null);
        Revalidate();
        if (_errors.Count > 0) return;
        if (TrySave(null)) Finish();
    }

    /// <summary>Starts over from the config as it is now (same as closing and reopening the window).</summary>
    void OnReload(object? sender, RoutedEventArgs e) => Reload();

    internal void Reload()
    {
        Draft = new SettingsDraft(_host.CurrentConfig, _host.ConfigFileHash());
        BuildPages();
        ShowConfigError();
        ShowBar(null);
        Revalidate();
    }

    internal void ResetToDefaults()
    {
        Draft.ResetToDefaults();
        BuildPages();
        Revalidate();
    }

    void OnCancel(object? sender, RoutedEventArgs e) => RequestClose();

    /// <summary>Cancel / Esc: closes, or asks inline first when there are unsaved changes.</summary>
    internal void RequestClose()
    {
        if (Draft.IsDirty || _general.HasLocalChanges) ShowBar(DiscardBar);
        else
        {
            _closeConfirmed = true;
            Close();
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closeConfirmed && (Draft.IsDirty || _general.HasLocalChanges))
        {
            e.Cancel = true;
            ShowBar(DiscardBar);
            return;
        }
        base.OnClosing(e);
    }

    internal void Discard()
    {
        _closeConfirmed = true;
        Close();
    }

    void OnDiscard(object? sender, RoutedEventArgs e) => Discard();

    void OnKeepEditing(object? sender, RoutedEventArgs e)
    {
        ShowBar(null);
        Revalidate();
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        RequestClose();
    }

    protected override void OnClosed(EventArgs e)
    {
        _debounce.Stop();
        base.OnClosed(e);
    }
}
