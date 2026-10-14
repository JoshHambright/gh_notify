using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GhNotify.Core;

namespace GhNotify.App;

public partial class PopupWindow : Window
{
    readonly IPopupHost _host;
    readonly DispatcherTimer _confirmTick;
    // Comment drafts survive re-renders (the row templates are rebuilt on every Changed / tick).
    readonly Dictionary<string, string> _comments = new();
    // Per-row pointer guard for the confirm button; reset on every render because the button is rebuilt.
    readonly Dictionary<string, ConfirmPointerGuard> _pointerGuards = new();
    string? _restoreFocusKey;
    IReadOnlyList<InboxSection>? _rendered;

    public PopupWindow(IPopupHost host)
    {
        InitializeComponent();
        _host = host;
        _confirmTick = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromMilliseconds(100) };
        _confirmTick.Tick += (_, _) => { if (IsVisible) Render(); else _confirmTick.Stop(); };
        _host.Changed += () => Dispatcher.InvokeAsync(() => { if (IsVisible) Render(); });
    }

    public DateTime LastHiddenByDeactivateUtc { get; private set; }

    public void Render()
    {
        var sections = _host.BuildSections();
        var rows = sections.SelectMany(s => s.Rows).ToList();

        _restoreFocusKey = Keyboard.FocusedElement is TextBox { Name: "CommentBox", Tag: string k } ? k : null;
        var open = rows.Where(r => r.Mode is RowMode.Confirming or RowMode.Sending).Select(r => r.Key).ToHashSet();
        foreach (var key in _comments.Keys.Where(key => !open.Contains(key)).ToList()) _comments.Remove(key);

        foreach (var key in _pointerGuards.Keys.Where(key => !open.Contains(key)).ToList()) _pointerGuards.Remove(key);

        // Rebuild row visuals only when something visible changed: replacing the buttons between
        // mouse-down and mouse-up (e.g. the refresh fired by opening the popup) swallows the click.
        if (!InboxSections.SameContent(_rendered, sections))
        {
            _rendered = sections;
            foreach (var g in _pointerGuards.Values) g.Reset();
            var offset = Scroller.VerticalOffset;
            Sections.ItemsSource = sections;
            if (offset > 0) Dispatcher.InvokeAsync(() => Scroller.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);
        }

        EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = _host.HeaderCount.ToString();
        ConfigErrorText.Text = _host.ConfigError;
        ConfigErrorText.Visibility = _host.ConfigError is null ? Visibility.Collapsed : Visibility.Visible;
        ThemeButton.ToolTip = $"Theme: {_host.ThemeMode}";
        RenderFooter();

        // While a confirm button is waiting out its 600 ms guard, re-render so it enables on time.
        var waiting = rows.Any(r => r.Mode == RowMode.Confirming && !r.ConfirmEnabled && !string.IsNullOrEmpty(r.ConfirmLabel));
        if (waiting && !_confirmTick.IsEnabled) _confirmTick.Start();
        else if (!waiting && _confirmTick.IsEnabled) _confirmTick.Stop();
    }

    void RenderFooter()
    {
        var inlines = FooterText.Inlines;
        inlines.Clear();
        inlines.Add(new Run(_host.IsRefreshing ? "Refreshing…"
            : _host.LastPolledAt is { } t ? $"Updated {t.ToLocalTime():HH:mm}" : "Not refreshed yet"));
        var sources = _host.Sources();
        AddSource(inlines, "GitHub", sources.Where(s => s.Name == "GitHub").ToList());
        AddSource(inlines, "ADO", sources.Where(s => s.Name == "ADO" || s.Name.StartsWith("ADO:", StringComparison.Ordinal)).ToList());
    }

    void AddSource(InlineCollection inlines, string label, IReadOnlyList<SourceStatus> group)
    {
        if (group.Count == 0) return;
        inlines.Add(new Run($" · {label} "));
        var bad = group.Where(s => !s.Healthy).ToList();
        if (bad.Count == 0)
        {
            inlines.Add(new Run("✓"));
            return;
        }
        var mark = new Run("✗") { FontWeight = FontWeights.Bold, ToolTip = string.Join("\n", bad.Select(s => $"{s.Name}: {s.Problem}")) };
        mark.SetResourceReference(TextElement.ForegroundProperty, "Brush.Red");
        inlines.Add(mark);
    }

    public void ShowNearTray()
    {
        _host.OnPopupShown();
        Render();
        var area = SystemParameters.WorkArea;
        MaxHeight = area.Height - 40;
        Left = area.Right - Width;
        Top = area.Bottom - (ActualHeight > 0 ? ActualHeight : MinHeight);
        Show();
        Activate();
    }

    void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // SizeToContent grows/shrinks the height (expanding a row); keep the popup anchored to the bottom of the work area.
        if (Left < SystemParameters.VirtualScreenLeft) return; // offscreen preview render
        Top = SystemParameters.WorkArea.Bottom - ActualHeight;
    }

    void OnBodySizeChanged(object sender, SizeChangedEventArgs e) =>
        Body.Clip = new RectangleGeometry(new Rect(e.NewSize), 12, 12);

    void OnActivated(object? sender, EventArgs e)
    {
        if (RefreshPolicy.ShouldRefreshOnOpen(_host.LastPolledAt, _host.IsRefreshing, DateTimeOffset.UtcNow))
            _ = _host.RefreshAsync();
    }

    void OnDeactivated(object? sender, EventArgs e)
    {
        LastHiddenByDeactivateUtc = DateTime.UtcNow;
        _confirmTick.Stop();
        Hide();
    }

    static string Key(object sender) => (string)((FrameworkElement)sender).Tag;

    void OnTitleClick(object sender, MouseButtonEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is string url) _host.OpenUrl(url);
    }

    void OnToggle(object sender, RoutedEventArgs e) => _host.ToggleExpand(Key(sender));

    void OnRunAgent(object sender, RoutedEventArgs e) => _host.RunAgent(Key(sender));

    void OnOpenInClaude(object sender, RoutedEventArgs e) => _host.OpenInClaude(Key(sender));

    void OnAction(object sender, RoutedEventArgs e)
    {
        var b = (Button)sender;
        if (b.Tag is string key && b.CommandParameter is ItemAction action)
        {
            _comments.Remove(key);
            _ = _host.BeginActionAsync(key, action);
        }
    }

    void OnCancel(object sender, RoutedEventArgs e) => _host.CancelAction(Key(sender));

    void OnRowMenu(object sender, RoutedEventArgs e)
    {
        if (RowMenuFor((UIElement)sender, Key(sender)) is { } menu) menu.IsOpen = true;
    }

    /// <summary>The row's snooze / mute menu, or null when the row is no longer shown. Internal for the preview renderer.</summary>
    internal ContextMenu? RowMenuFor(UIElement target, string key)
    {
        var row = _rendered?.SelectMany(s => s.Rows).FirstOrDefault(r => r.Key == key);
        return row is null ? null : MenuOf(target, key, RowMenus.For(row));
    }

    /// <summary>A context menu of row menu entries for <paramref name="key"/> (the preview renderer also draws a submenu with it).</summary>
    internal ContextMenu MenuOf(UIElement target, string key, IEnumerable<RowMenuEntry> entries)
    {
        var menu = new ContextMenu { PlacementTarget = target };
        foreach (var entry in entries) menu.Items.Add(MenuItemFor(key, entry));
        return menu;
    }

    MenuItem MenuItemFor(string key, RowMenuEntry entry)
    {
        var item = new MenuItem { Header = entry.Header };
        foreach (var child in entry.Children) item.Items.Add(MenuItemFor(key, child));
        if (entry.Command is { } cmd)
            item.Click += (_, _) =>
            {
                switch (cmd.Kind)
                {
                    case RowMenuKind.Snooze: _host.SnoozeItem(key, cmd.Snooze!.Value); break;
                    case RowMenuKind.Mute: _host.Mute(key, cmd.Scope!.Value, cmd.Duration!.Value); break;
                    case RowMenuKind.Unsnooze: _host.Unsnooze(key); break;
                }
            };
        return item;
    }

    void OnUnsnooze(object sender, RoutedEventArgs e) => _host.Unsnooze(Key(sender));

    void OnToggleSection(object sender, RoutedEventArgs e) => _host.ToggleSection(Key(sender));

    void OnConfirm(object sender, RoutedEventArgs e)
    {
        var key = Key(sender);
        // A mouse click only counts once the pointer moved onto the enabled button (see ConfirmPointerGuard); keyboard is fine.
        if (InputManager.Current.MostRecentInputDevice is MouseDevice && !Guard(key).IsArmed) return;
        _ = _host.ConfirmActionAsync(key, _comments.GetValueOrDefault(key));
    }

    ConfirmPointerGuard Guard(string key)
    {
        if (!_pointerGuards.TryGetValue(key, out var g)) _pointerGuards[key] = g = new ConfirmPointerGuard();
        return g;
    }

    static Point ScreenPos(object sender, MouseEventArgs e)
    {
        var b = (Button)sender;
        return b.PointToScreen(e.GetPosition(b));
    }

    void OnConfirmMouseEnter(object sender, MouseEventArgs e) =>
        Guard(Key(sender)).Entered(ScreenPos(sender, e), ((Button)sender).IsEnabled);

    void OnConfirmMouseMove(object sender, MouseEventArgs e) =>
        Guard(Key(sender)).Moved(ScreenPos(sender, e), ((Button)sender).IsEnabled);

    void OnConfirmMouseLeave(object sender, MouseEventArgs e) => Guard(Key(sender)).Left();

    void OnConfirmEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is string key) Guard(key).Reset();
    }

    void OnConfirmPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!Guard(Key(sender)).IsArmed) e.Handled = true; // swallow the press so no Click is raised
    }

    void OnCopy(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is string text) _host.CopyText(text);
    }

    void OnCommentLoaded(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender;
        if (box.Tag is not string key) return;
        if (_comments.TryGetValue(key, out var draft) && box.Text != draft)
        {
            box.Text = draft;
            box.CaretIndex = draft.Length;
        }
        if (key == _restoreFocusKey) box.Focus();
    }

    void OnCommentChanged(object sender, TextChangedEventArgs e)
    {
        var box = (TextBox)sender;
        if (box.Tag is string key) _comments[key] = box.Text;
    }

    void OnRefresh(object sender, RoutedEventArgs e) => _ = _host.RefreshAsync();

    void OnCycleTheme(object sender, RoutedEventArgs e) => _host.CycleTheme();

    void OnMenu(object sender, RoutedEventArgs e) => FooterMenu((UIElement)sender).IsOpen = true;

    /// <summary>The footer ⋯ menu. Internal for the preview renderer.</summary>
    internal ContextMenu FooterMenu(UIElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target };
        var openConfig = new MenuItem { Header = "Open config" };
        openConfig.Click += (_, _) => _host.OpenConfig();
        var startup = new MenuItem { Header = "Start with Windows", IsCheckable = true, IsChecked = StartupRegistration.IsEnabled() };
        startup.Click += (_, _) => StartupRegistration.Set(startup.IsChecked);
        var logs = new MenuItem { Header = "Open logs folder" };
        logs.Click += (_, _) => _host.OpenLogs();
        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, _) => _host.Quit();
        menu.Items.Add(openConfig);
        menu.Items.Add(startup);
        menu.Items.Add(logs);
        menu.Items.Add(MutedMenu());
        menu.Items.Add(new Separator());
        menu.Items.Add(quit);
        return menu;
    }

    /// <summary>"Muted (n)" with an Unmute entry per active mute; a disabled "Nothing muted" when there are none.</summary>
    MenuItem MutedMenu()
    {
        var mutes = _host.ActiveMutes();
        if (mutes.Count == 0) return new MenuItem { Header = "Nothing muted", IsEnabled = false };
        var item = new MenuItem { Header = $"Muted ({mutes.Count})" };
        foreach (var m in mutes)
        {
            // "__": a single underscore in a MenuItem header marks an access key and is not shown.
            var unmute = new MenuItem { Header = $"Unmute {m.Pattern} · {m.Remaining}".Replace("_", "__") };
            unmute.Click += (_, _) => _host.Unmute(m.Pattern);
            item.Items.Add(unmute);
        }
        return item;
    }
}
