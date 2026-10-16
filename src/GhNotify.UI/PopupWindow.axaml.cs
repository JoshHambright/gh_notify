using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GhNotify.Core;

namespace GhNotify.UI;

/// <summary>
/// The tray popup: borderless, topmost, hides when it loses focus. This file holds the window, rendering and footer; keyboard,
/// search, selection and the merge options are in PopupWindow.Input.cs, and the batch approve/reject panel in PopupWindow.Batch.cs.
/// </summary>
public partial class PopupWindow : Window
{
    readonly IPopupHost _host;
    readonly DispatcherTimer _confirmTick;
    // Comment drafts survive re-renders (the row templates are rebuilt when anything visible changes).
    readonly Dictionary<string, string> _comments = new();
    // Per-row pointer guard for the confirm button; reset on every rebuild because the button is rebuilt.
    readonly Dictionary<string, ConfirmPointerGuard> _pointerGuards = new();
    // Merge choices made in the confirm panel (survive re-renders like the comment drafts).
    readonly Dictionary<string, MergeMethod> _methods = new();
    readonly Dictionary<string, bool> _deleteBranch = new();
    readonly DispatcherTimer _noticeTimer;
    string? _restoreFocusKey;
    int _restoreCaret;
    IReadOnlyList<InboxSection>? _rendered;
    IReadOnlyList<InboxRow> _rows = [];
    bool _confirmingRestart;
    string? _footerSignature;
    string? _configErrorSignature;
    // Search query (cleared when the popup hides) and the keyboard selection, keyed by item key so a refresh can't move it.
    string _query = "";
    string? _selectedKey;
    int _selectedIndex;
    // Where the popup sits: the primary screen's work area, bottom-right (set when it is shown).
    PixelRect? _area;

    public PopupWindow() : this(new SampleHost()) { }

    public PopupWindow(IPopupHost host)
    {
        _host = host;
        InitializeComponent();
        _confirmTick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _confirmTick.Tick += (_, _) =>
        {
            if (IsVisible) Render();
            else _confirmTick.Stop();
        };
        _host.Changed += () => Dispatcher.UIThread.Post(() => { if (IsVisible) Render(); });
        _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            NoticeText.IsVisible = false;
        };
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnPreviewTextInput, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);
        Activated += OnActivated;
        Deactivated += OnDeactivated;
        SizeChanged += (_, _) => { if (IsVisible) PlaceBottomRight(); };
        InitResizeGrip();
    }

    public IPopupHost Host => _host;

    /// <summary>When the popup last hid itself because it lost focus; a tray click right after that must not re-open it.</summary>
    public DateTime LastHiddenByDeactivateUtc { get; private set; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && change.NewValue is false) ResetTransientState();
    }

    /// <summary>Hiding the popup clears the search and any notice; the selection restarts on the first row next time.</summary>
    void ResetTransientState()
    {
        _query = "";
        SearchBox.Clear();
        SearchBar.IsVisible = false;
        _noticeTimer.Stop();
        NoticeText.IsVisible = false;
        _selectedKey = null;
        _selectedIndex = 0;
        _confirmTick.Stop();
    }

    /// <summary>Syncs the visuals with the host. Cheap when nothing visible changed: the row visuals are rebuilt only when the content differs.</summary>
    public void Render()
    {
        var sections = _host.BuildSections(string.IsNullOrWhiteSpace(_query) ? null : _query);
        var rows = sections.SelectMany(s => s.Rows).ToList();
        _rows = rows;
        var batch = _host.Batch;

        // A rebuild detaches the batch panel; remember its comment box focus so it can be restored once re-attached.
        if (_batchComment is { IsKeyboardFocusWithin: true } bc) _batchCommentCaret = bc.CaretIndex;
        var focused = FocusedElement();
        if (focused is TextBox { Name: "CommentBox", Tag: string k } box)
        {
            _restoreFocusKey = k;
            _restoreCaret = box.CaretIndex;
        }
        else _restoreFocusKey = null;
        var open = rows.Where(r => r.Mode is RowMode.Confirming or RowMode.Sending).Select(r => r.Key).ToHashSet();
        foreach (var key in _comments.Keys.Where(key => !open.Contains(key)).ToList()) _comments.Remove(key);
        foreach (var key in _methods.Keys.Where(key => !open.Contains(key)).ToList()) _methods.Remove(key);
        foreach (var key in _deleteBranch.Keys.Where(key => !open.Contains(key)).ToList()) _deleteBranch.Remove(key);
        foreach (var key in _pointerGuards.Keys.Where(key => !open.Contains(key) && !(key == BatchGuardKey && batch.Active)).ToList()) _pointerGuards.Remove(key);

        // Rebuild row visuals only when something visible changed: replacing the buttons between
        // pointer-down and pointer-up (e.g. the refresh fired by opening the popup) swallows the click.
        if (!InboxSections.SameContent(_rendered, sections))
        {
            _rendered = sections;
            foreach (var g in _pointerGuards.Values) g.Reset();
            var offset = Scroller.Offset.Y;
            Sections.ItemsSource = sections;
            if (offset > 0) Dispatcher.UIThread.Post(() => Scroller.Offset = new Vector(0, offset), DispatcherPriority.Loaded);
        }

        var searching = !string.IsNullOrWhiteSpace(_query);
        EmptyState.IsVisible = rows.Count == 0 && !searching;
        NoMatchesText.IsVisible = rows.Count == 0 && searching;
        UpdateBatchPanel(sections);
        EnsureSelection(rows);
        ApplySelection(scrollIntoView: false);
        Dispatcher.UIThread.Post(() => ApplySelection(scrollIntoView: false), DispatcherPriority.Loaded);
        CountText.Text = _host.HeaderCount.ToString();
        RenderConfigError();
        ApplySize();
        ThemeButton.SetValue(ToolTip.TipProperty, $"Theme: {_host.ThemeMode}");
        RenderFooter();

        // While a confirm button is waiting out its 600 ms guard, re-render so it enables on time.
        var waiting = rows.Any(r => r.Mode == RowMode.Confirming && !r.ConfirmEnabled && !string.IsNullOrEmpty(r.ConfirmLabel))
            || batch is { Phase: BatchPhase.Ready, CheckedCount: > 0 } && !batch.CanConfirm(DateTimeOffset.Now);
        if (waiting && !_confirmTick.IsEnabled) _confirmTick.Start();
        else if (!waiting && _confirmTick.IsEnabled) _confirmTick.Stop();
    }

    IInputElement? FocusedElement() => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();

    /// <summary>Binds a property to a theme token so it follows the light / dark variant.</summary>
    void BindToken(AvaloniaObject target, AvaloniaProperty property, string token) =>
        target.Bind(property, this.GetResourceObservable(token));

    // ----- footer -----

    /// <summary>The config-error banner: the error, then a "Fix in Settings" link. Rebuilt only when the error changes.</summary>
    void RenderConfigError()
    {
        var error = _host.ConfigError;
        ConfigErrorText.IsVisible = error is not null;
        if (error == _configErrorSignature) return;
        _configErrorSignature = error;
        var inlines = ConfigErrorText.Inlines ??= new InlineCollection();
        inlines.Clear();
        if (error is null) return;
        inlines.Add(new Run(error + " "));
        inlines.Add(UpdateLink("Fix in Settings", () => _host.OpenSettings(), bold: true));
    }

    void RenderFooter()
    {
        var statusText = _host.IsRefreshing ? "Refreshing…"
            : _host.LastPolledAt is { } t ? $"Updated {t.ToLocalTime():HH:mm}" : "Not refreshed yet";
        var sources = _host.Sources();
        var update = _host.Update;
        var agents = _host.RunningAgents;
        if (update.Phase != UpdatePhase.Ready || agents == 0) _confirmingRestart = false;
        var signature = UpdateUi.FooterSignature(statusText, sources, update, _confirmingRestart, agents, _host.WhatsNewVersion, _host.JustHidden);
        if (signature == _footerSignature) return;
        _footerSignature = signature;
        var inlines = FooterText.Inlines ??= new InlineCollection();
        inlines.Clear();
        inlines.Add(new Run(statusText));
        AddSource(inlines, "GitHub", sources.Where(s => s.Name == "GitHub").ToList());
        AddSource(inlines, "ADO", sources.Where(s => s.Name == "ADO" || s.Name.StartsWith("ADO:", StringComparison.Ordinal)).ToList());
        AddUndoHide(inlines);
        AddUpdateLink(inlines);
    }

    /// <summary>"Hidden gh:acme/legacy · Undo" right after a hide, until the popup is reopened.</summary>
    void AddUndoHide(InlineCollection inlines)
    {
        if (_host.JustHidden is not { } pattern) return;
        inlines.Add(new Run(" · " + UpdateUi.HiddenNotice(pattern) + " "));
        inlines.Add(UpdateLink(UpdateUi.Undo, () => _host.Unhide(pattern), bold: true));
    }

    void AddUpdateLink(InlineCollection inlines)
    {
        var status = _host.Update;
        var agents = _host.RunningAgents;
        if (status.Phase != UpdatePhase.Ready || agents == 0) _confirmingRestart = false;
        if (UpdateUi.FooterLink(status) is not { } text)
        {
            AddWhatsNewLink(inlines);
            return;
        }
        inlines.Add(new Run(" · "));
        if (!_confirmingRestart)
        {
            inlines.Add(UpdateLink(text, OnRestartClick));
            return;
        }
        inlines.Add(new Run(UpdateUi.ConfirmPrompt(agents)));
        inlines.Add(UpdateLink(UpdateUi.RestartAnyway, () => _host.RestartToUpdate(true)));
        inlines.Add(new Run(" · "));
        inlines.Add(UpdateLink(UpdateUi.Cancel, () =>
        {
            _confirmingRestart = false;
            RenderFooter();
        }));
    }

    /// <summary>"Updated to vX · What's new" after an update, with ✕ to dismiss; both clear it for good.</summary>
    void AddWhatsNewLink(InlineCollection inlines)
    {
        if (_host.WhatsNewVersion is not { } version) return;
        inlines.Add(new Run(" · "));
        inlines.Add(UpdateLink(UpdateUi.WhatsNewLink(version), _host.OpenWhatsNew));
        inlines.Add(new Run(" "));
        var dismiss = UpdateLink("✕", _host.DismissWhatsNew);
        if (dismiss.Child is Button b) ToolTip.SetTip(b, "Dismiss");
        inlines.Add(dismiss);
    }

    /// <summary>A red text link in the footer (a flat button, so it is keyboard-reachable too).</summary>
    InlineUIContainer UpdateLink(string text, Action click, bool bold = false)
    {
        var link = new Button
        {
            Theme = (Avalonia.Styling.ControlTheme)this.FindResource("Style.LinkButton")!,
            Content = text,
            FontSize = 11,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            Padding = new Thickness(0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
        };
        link.Click += (_, _) => click();
        return new InlineUIContainer(link) { BaselineAlignment = BaselineAlignment.Baseline };
    }

    void OnRestartClick()
    {
        if (_host.RestartToUpdate(false) == RestartDecision.NeedsConfirm) _confirmingRestart = true;
        RenderFooter();
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
        var mark = new TextBlock { Text = "✗", FontWeight = FontWeight.Bold, FontSize = 11 };
        ToolTip.SetTip(mark, string.Join("\n", bad.Select(s => $"{s.Name}: {s.Problem}")));
        BindToken(mark, TextBlock.ForegroundProperty, "Brush.Red");
        inlines.Add(new InlineUIContainer(mark));
    }

    // ----- show / hide / placement -----

    /// <summary>Shows the popup at the bottom-right of the primary screen's work area and re-arms any open confirm panel.</summary>
    public void ShowNearTray()
    {
        _selectedKey = null; // opening selects the first row
        _selectedIndex = 0;
        _host.OnPopupShown();
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is not null)
        {
            _area = screen.WorkingArea;
            MaxHeight = Math.Max(MinHeight, _area.Value.Height / screen.Scaling - 40);
        }
        Render();
        PlaceBottomRight();
        Show();
        Activate();
        PlaceBottomRight();
    }

    /// <summary>SizeToContent grows and shrinks the height (expanding a row); keep the popup anchored to the bottom of the work area.</summary>
    void PlaceBottomRight()
    {
        if (_area is not { } area) return;
        var scale = RenderScaling > 0 ? RenderScaling : 1;
        var height = Bounds.Height > 0 ? Bounds.Height : MinHeight;
        Position = new PixelPoint(
            area.Right - (int)Math.Round(Width * scale),
            area.Bottom - (int)Math.Round(height * scale));
    }

    void OnActivated(object? sender, EventArgs e)
    {
        if (RefreshPolicy.ShouldRefreshOnOpen(_host.LastPolledAt, _host.IsRefreshing, DateTimeOffset.UtcNow))
            _ = _host.RefreshAsync();
    }

    void OnDeactivated(object? sender, EventArgs e)
    {
        // A deactivation that arrives after the popup was hidden and shown again is stale (IsActive is already true again).
        if (!IsVisible || IsActive) return;
        LastHiddenByDeactivateUtc = DateTime.UtcNow;
        _confirmTick.Stop();
        Hide();
    }

    // ----- row handlers -----

    /// <summary>Runs the action with the control's row key; a control whose row was just removed (no key) does nothing.</summary>
    static void WithKey(object? sender, Action<string> action)
    {
        if (RowKey(sender) is { } key) action(key);
    }

    void OnTitleClick(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && ((Control)sender!).Tag is string url) _host.OpenUrl(url);
    }

    void OnToggle(object? sender, RoutedEventArgs e) => WithKey(sender, _host.ToggleExpand);

    void OnRunAgent(object? sender, RoutedEventArgs e) => WithKey(sender, _host.RunAgent);

    void OnOpenInClaude(object? sender, RoutedEventArgs e) => WithKey(sender, _host.OpenInClaude);

    void OnAction(object? sender, RoutedEventArgs e)
    {
        var b = (Button)sender!;
        if (b.Tag is string key && b.CommandParameter is ItemAction action)
        {
            _comments.Remove(key);
            _methods.Remove(key);
            _deleteBranch.Remove(key);
            _ = _host.BeginActionAsync(key, action);
        }
    }

    void OnCancel(object? sender, RoutedEventArgs e) => WithKey(sender, _host.CancelAction);

    void OnRowMenu(object? sender, RoutedEventArgs e)
    {
        if (RowKey(sender) is { } key && RowMenuFor((Control)sender!, key) is { } menu) ShowMenu(menu, (Control)sender!);
    }

    /// <summary>The row's snooze / mute menu, or null when the row is no longer shown.</summary>
    internal ContextMenu? RowMenuFor(Control target, string key)
    {
        var row = _rendered?.SelectMany(s => s.Rows).FirstOrDefault(r => r.Key == key);
        return row is null ? null : MenuOf(key, RowMenus.For(row));
    }

    ContextMenu MenuOf(string key, IEnumerable<RowMenuEntry> entries)
    {
        var menu = new ContextMenu();
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
                    case RowMenuKind.Hide: _host.HideMatching(key, cmd.Scope!.Value); break;
                }
            };
        return item;
    }

    void OnUnsnooze(object? sender, RoutedEventArgs e) => WithKey(sender, _host.Unsnooze);

    void OnToggleSection(object? sender, RoutedEventArgs e) => WithKey(sender, _host.ToggleSection);

    void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (((Control)sender!).Tag is string text) _host.CopyText(text);
    }

    void OnRefresh(object? sender, RoutedEventArgs e) => _ = _host.RefreshAsync();

    void OnCycleTheme(object? sender, RoutedEventArgs e) => _host.CycleTheme();

    void OnMenu(object? sender, RoutedEventArgs e) => ShowMenu(FooterMenu(), (Control)sender!);

    /// <summary>The menu most recently opened (row, snooze or footer); tests read it.</summary>
    internal ContextMenu? OpenMenu { get; private set; }

    void ShowMenu(ContextMenu menu, Control target)
    {
        OpenMenu = menu;
        menu.Open(target);
    }

    /// <summary>The footer ⋯ menu.</summary>
    internal ContextMenu FooterMenu()
    {
        var menu = new ContextMenu();
        var settings = new MenuItem { Header = "Settings…" };
        settings.Click += (_, _) => _host.OpenSettings();
        menu.Items.Add(settings);
        var openConfig = new MenuItem { Header = "Open config" };
        openConfig.Click += (_, _) => _host.OpenConfig();
        var startup = new MenuItem
        {
            Header = "Start with Windows",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _host.StartWithWindows,
        };
        startup.Click += (_, _) => _host.StartWithWindows = !_host.StartWithWindows;
        var logs = new MenuItem { Header = "Open logs folder" };
        logs.Click += (_, _) => _host.OpenLogs();
        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, _) => _host.Quit();
        // "__": a single underscore in a MenuItem header marks an access key and is not shown.
        menu.Items.Add(new MenuItem { Header = UpdateUi.VersionHeader(_host.Version).Replace("_", "__"), IsEnabled = false });
        var (checkHeader, checkEnabled, checkTip) = UpdateUi.CheckItem(_host.Update);
        var check = new MenuItem { Header = checkHeader, IsEnabled = checkEnabled };
        ToolTip.SetTip(check, checkTip);
        ToolTip.SetShowOnDisabled(check, true);
        check.Click += (_, _) => _ = _host.CheckForUpdatesAsync();
        menu.Items.Add(check);
        if (UpdateUi.RestartItem(_host.Update) is { } restartHeader)
        {
            var restart = new MenuItem { Header = restartHeader };
            restart.Click += (_, _) => OnRestartClick();
            menu.Items.Add(restart);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(openConfig);
        menu.Items.Add(startup);
        menu.Items.Add(logs);
        menu.Items.Add(MutedMenu());
        menu.Items.Add(HiddenMenu());
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
            var unmute = new MenuItem { Header = $"Unmute {m.Pattern} · {m.Remaining}".Replace("_", "__") };
            unmute.Click += (_, _) => _host.Unmute(m.Pattern);
            item.Items.Add(unmute);
        }
        return item;
    }

    /// <summary>"Hidden (n)" with an Unhide entry per filters.hide pattern; a disabled "Nothing hidden" when there are none.</summary>
    MenuItem HiddenMenu()
    {
        var hidden = _host.HiddenFilters();
        if (hidden.Count == 0) return new MenuItem { Header = "Nothing hidden", IsEnabled = false };
        var item = new MenuItem { Header = $"Hidden ({hidden.Count})" };
        foreach (var h in hidden)
        {
            var unhide = new MenuItem { Header = UpdateUi.UnhideItem(h.Pattern, h.Items).Replace("_", "__") };
            unhide.Click += (_, _) => _host.Unhide(h.Pattern);
            item.Items.Add(unhide);
        }
        return item;
    }

    // ----- confirm panel -----

    void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (RowKey(sender) is not { } key) return;
        // A pointer click only counts once the pointer moved onto the enabled button (see ConfirmPointerGuard); keyboard is fine.
        if (_pointerInput && !Guard(key).IsArmed) return;
        _ = _host.ConfirmActionAsync(key, InputFor(key));
    }

    /// <summary>What the user chose in the row's confirm panel: the comment, and for merge the method and Delete branch.</summary>
    ActionInput InputFor(string key)
    {
        var options = _rows.FirstOrDefault(r => r.Key == key)?.Options;
        return new ActionInput(_comments.GetValueOrDefault(key),
            _methods.TryGetValue(key, out var m) ? m : null,
            _deleteBranch.TryGetValue(key, out var d) ? d : options?.DeleteBranch ?? false);
    }

    ConfirmPointerGuard Guard(string key)
    {
        if (!_pointerGuards.TryGetValue(key, out var g)) _pointerGuards[key] = g = new ConfirmPointerGuard();
        return g;
    }

    // The pointer guard hooks a confirm button once, however often it is re-attached to the tree.
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, object> _hooked = new();

    void OnConfirmLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b) HookConfirm(b);
    }

    void HookConfirm(Button b)
    {
        if (!_hooked.TryAdd(b, this)) return;
        b.PointerEntered += OnConfirmPointerEntered;
        b.PointerMoved += OnConfirmPointerMoved;
        b.PointerExited += OnConfirmPointerExited;
        b.AddHandler(PointerPressedEvent, OnConfirmPointerPressed, RoutingStrategies.Tunnel);
        b.PropertyChanged += (_, a) =>
        {
            // The button was enabled or disabled: any pointer progress toward it no longer counts.
            if (a.Property == IsEnabledProperty && b.Tag is string key) Guard(key).Reset();
        };
    }

    static ScreenPoint ScreenPos(object? sender, PointerEventArgs e)
    {
        var b = (Button)sender!;
        var p = b.PointToScreen(e.GetPosition(b));
        return new ScreenPoint(p.X, p.Y);
    }

    // A confirm button loses its key (Tag) when its row is removed or recycled, for example right after the action
    // completes, while the pointer can still be over it. Pointer events for such a button are ignored: an exception
    // here is thrown inside Avalonia's pointer-over tracking and repeats on every later mouse move, freezing the popup.
    static string? RowKey(object? sender) => (sender as Control)?.Tag as string;

    void OnConfirmPointerEntered(object? sender, PointerEventArgs e)
    {
        if (RowKey(sender) is { } key) Guard(key).Entered(ScreenPos(sender, e), ((Button)sender!).IsEnabled);
    }

    void OnConfirmPointerMoved(object? sender, PointerEventArgs e)
    {
        if (RowKey(sender) is { } key) Guard(key).Moved(ScreenPos(sender, e), ((Button)sender!).IsEnabled);
    }

    void OnConfirmPointerExited(object? sender, PointerEventArgs e)
    {
        if (RowKey(sender) is { } key) Guard(key).Left();
    }

    void OnConfirmPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Swallow an unarmed press so no click is raised: it must start on a confirm button the pointer really moved onto.
        // A button with no key belongs to no live row, so its press is always swallowed.
        if (e.GetCurrentPoint((Control)sender!).Properties.IsLeftButtonPressed &&
            (RowKey(sender) is not { } key || !Guard(key).IsArmed)) e.Handled = true;
    }

    void OnCommentLoaded(object? sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender!;
        if (box.Tag is not string key) return;
        if (_comments.TryGetValue(key, out var draft) && box.Text != draft)
        {
            box.Text = draft;
            box.CaretIndex = draft.Length;
        }
        if (key == _restoreFocusKey)
        {
            box.Focus();
            box.CaretIndex = Math.Min(_restoreCaret, box.Text?.Length ?? 0);
        }
    }

    void OnCommentChanged(object? sender, TextChangedEventArgs e)
    {
        var box = (TextBox)sender!;
        if (box.Tag is string key) _comments[key] = box.Text ?? "";
    }
}
