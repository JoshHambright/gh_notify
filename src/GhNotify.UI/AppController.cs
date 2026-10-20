using System.Collections.Concurrent;
using GhNotify.Ado;
using GhNotify.Agents;
using GhNotify.Core;
using GhNotify.GitHub;
using ThemeMode = GhNotify.Core.ThemeMode;

namespace GhNotify.UI;

public sealed class AppController : IPopupHost, ISettingsHost, IDisposable
{
    readonly ConfigStore _config;
    readonly StateStore _stateStore;
    readonly Reconciler _reconciler;
    readonly Poller _poller;
    readonly RepoLocator _repos;
    readonly AgentCoordinator _agents;
    readonly HookDispatcher _hooks;
    readonly INotifier _toasts;
    readonly PlatformServices _platform;
    readonly AppRuntime _runtime;
    readonly IClaudeLauncher _launcher;
    readonly CancellationTokenSource _cts = new();
    readonly object _saveGate = new();
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _announced = new();
    readonly IItemActions _actions;
    readonly ConcurrentDictionary<string, RowUiState> _rowStates = new();
    // Items whose action finished (Done) but that a later poll still reports (e.g. search-index lag): hidden until they change or leave.
    readonly ConcurrentDictionary<string, byte> _doneHidden = new();
    static readonly TimeSpan PreviewTimeout = TimeSpan.FromSeconds(20);
    int _refreshing;
    ThemeMode _themeMode;
    bool _claudeAvailable;
    // Guards the four filter fields below (mutes, snoozes, section toggles).
    readonly object _filterGate = new();
    List<TimedMute> _mutes;
    List<Snooze> _snoozes;
    bool _waitingExpanded;
    bool _snoozedExpanded;
    bool _mutedExpanded;
    // The other sections (Approvals, Review requests, …) start expanded; these are the ones the user collapsed.
    readonly HashSet<string> _collapsed;
    string? _justHidden;
    double _popupWidth;
    double _textScale;
    bool _commentsWarningShown;
    readonly SkillLocator _skills = new(SkillLocator.DefaultClaudeHome);

    public event Action? Changed;
    /// <summary>The popup asked for the settings window (⋯ → Settings…, or "Fix in Settings"); App opens it.</summary>
    public event Action? SettingsRequested;
    /// <summary>Raised (on the caller's thread) after <see cref="CycleTheme"/> changes the persisted mode.</summary>
    public event Action? ThemeModeChanged;

    UpdateService? _updates;
    GlobalHotkey? _hotkey;
    public AppConfig Config => _config.Current;
    /// <summary>Attaches the background update service (created after the controller because it reads <see cref="Config"/>).</summary>
    public void AttachUpdates(UpdateService updates) => _updates = updates;
    /// <summary>Attaches the global hotkey so config changes re-register it and Settings can show why it failed.</summary>
    public void AttachHotkey(GlobalHotkey hotkey) => _hotkey = hotkey;
    public string? HotkeyError => _hotkey?.Error;
    /// <summary>The app version shown in the popup's ⋯ menu; set by App from the host options.</summary>
    public string Version { get; set; } = "dev";
    public void RaiseChanged() => Changed?.Invoke();
    public UpdateStatus Update => _updates?.Status ?? new UpdateStatus(UpdatePhase.Off, null, null, null, null);
    public int RunningAgents => _agents.Runs.Values.Count(r => r.State is AgentState.Running or AgentState.Queued);
    public Task CheckForUpdatesAsync() => _updates?.CheckNowAsync(_cts.Token) ?? Task.CompletedTask;

    public RestartDecision RestartToUpdate(bool confirmed)
    {
        var d = _updates?.RequestRestart(RunningAgents, confirmed) ?? RestartDecision.NothingToApply;
        if (d == RestartDecision.Restarting) _runtime.Quit();
        return d;
    }

    public AppController(ConfigStore config, StateStore stateStore, PlatformServices platform, AppRuntime runtime)
    {
        _platform = platform;
        _runtime = runtime;
        _toasts = platform.Notifier;
        _config = config;
        _stateStore = stateStore;
        var state = stateStore.Load();
        _themeMode = state.ThemeMode;
        _mutes = state.Mutes.ToList();
        _snoozes = state.Snoozes.ToList();
        _waitingExpanded = state.WaitingExpanded;
        _snoozedExpanded = state.SnoozedExpanded;
        _mutedExpanded = state.MutedExpanded;
        _collapsed = new HashSet<string>(state.CollapsedSections);
        _commentsWarningShown = state.CommentsWarningShown;
        _lastRunVersion = state.LastRunVersion;
        _whatsNewVersion = state.WhatsNewVersion;
        _popupWidth = PopupSize.ClampWidth(state.PopupWidth ?? PopupSize.DefaultWidth);
        _textScale = PopupSize.NormalizeScale(state.TextScale);
        _usedBefore = state.Snapshot.Count > 0 || state.AgentRuns.Count > 0 || state.Mutes.Count > 0 || state.Snoozes.Count > 0;
        _reconciler = new Reconciler(state.Snapshot.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<InboxItem>)kv.Value));

        var runner = new ProcessRunner();
        var ghClient = new GitHubClient(new HttpClient { Timeout = TimeSpan.FromSeconds(25) }, new GhTokenProvider(runner));
        var ghSource = new GitHubSource(
            ghClient,
            () => _config.Current.GitHub,
            () => _reconciler.ItemsFor("GitHub"));
        var adoClient = new AdoClient(new HttpClient { Timeout = TimeSpan.FromSeconds(25) }, new AzTokenProvider(runner, config: () => _config.Current.Ado));
        var directory = new AdoDirectory(adoClient);
        _actions = new CompositeItemActions(new Dictionary<Provider, IItemActions>
        {
            [Provider.GitHub] = new GitHubItemActions(ghClient),
            [Provider.Ado] = new AdoItemActions(adoClient, directory),
        });
        Batch = new BatchCoordinator(_actions, PreviewTimeout);
        Batch.Changed += () => Changed?.Invoke();
        var catalog = new CompositeCatalog(
            new StaticCatalog(() => _config.Current.GitHub.Enabled ? [ghSource] : []),
            new AdoSourceCatalog(directory, () => _config.Current.Ado,
                org => new AdoOrgSource(org, adoClient, directory, () => _config.Current.Ado,
                    () => _reconciler.ItemsFor($"ADO:{org}"))));
        _poller = new Poller(catalog, _reconciler, TimeSpan.FromSeconds(30));
        _poller.Polled += HandleDiff;

        _repos = new RepoLocator(() => _config.Current.RepoRoots);
        _claudeAvailable = CommandResolver.Find("claude") is not null;
        var deps = new AgentDeps(_repos, new WorktreeManager(runner, runtime.Dirs.Worktrees), new ClaudeRunner(runner),
            new SkillLocator(SkillLocator.DefaultClaudeHome), () => _claudeAvailable, new CommandRunner(runner));
        _agents = new AgentCoordinator(deps, () => _config.Current, runtime.Dirs.Scratch, restored: state.AgentRuns);
        foreach (var r in state.AgentRuns)
            if (r.FinishedAt is { } f) _announced[r.Key] = f; // don't re-announce runs finished before a restart
        _agents.RunChanged += OnRunChanged;
        _agents.RunRemoved += _ => { Save(); Changed?.Invoke(); };
        _hooks = new HookDispatcher(runner, () => _config.Current);
        _launcher = new ClaudeLauncher(platform.Shell.OpenTerminal);
    }

    public DateTimeOffset? LastPolledAt => _poller.LastPolledAt;
    public string ConfigPath => _config.Path;

    public void Start()
    {
        // Only an installed build announces "What's new"; a dev build leaves the remembered versions alone.
        if (_updates is not null && Update.Phase != UpdatePhase.DevBuild)
        {
            var (announce, lastRun) = WhatsNew.OnStart(_lastRunVersion, _whatsNewVersion, Version, _usedBefore);
            if (announce != _whatsNewVersion || lastRun != _lastRunVersion)
            {
                (_whatsNewVersion, _lastRunVersion) = (announce, lastRun);
                Save();
            }
        }
        HandleDiff(_reconciler.RetireWhere(IsDisabledSource));
        _ = _poller.RunAsync(() => TimeSpan.FromSeconds(_config.Current.PollSeconds), _cts.Token);
    }

    public bool IsRefreshing => Volatile.Read(ref _refreshing) > 0;

    public async Task RefreshAsync()
    {
        Interlocked.Increment(ref _refreshing); // set synchronously so a second open/focus in the same tick sees it
        Changed?.Invoke();
        try { await _poller.PollOnceAsync(_cts.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Log.Error("manual refresh failed", ex); }
        catch (OperationCanceledException) { }
        finally
        {
            Interlocked.Decrement(ref _refreshing);
            Changed?.Invoke();
        }
    }

    public void ReloadConfig()
    {
        if (_config.IsCurrent) return; // e.g. the watcher seeing our own Settings save: already loaded
        _config.Load();
        AfterConfigChanged();
    }

    /// <summary>Applies a newly loaded config: retires disabled sources, re-renders and polls.</summary>
    void AfterConfigChanged()
    {
        _claudeAvailable = CommandResolver.Find("claude") is not null;
        HandleDiff(_reconciler.RetireWhere(IsDisabledSource));
        _hotkey?.Apply(_config.Current.Hotkey);
        Changed?.Invoke();
        _ = RefreshAsync();
    }

    bool IsDisabledSource(string name) => SourceRetirement.IsDisabled(name, _config.Current);

    void HandleDiff(InboxDiff diff)
    {
        ProcessDiff(diff);
        // A source disabled (config reload) while a poll was in flight is re-added by that poll's diff; retire it again.
        var disabled = _reconciler.RetireWhere(IsDisabledSource);
        if (disabled.Removed.Count > 0) ProcessDiff(disabled);
        // Once a real ADO:<org> source is healthy, the "ADO" pseudo-source (discovery failure placeholder) is superseded.
        if (SourceRetirement.ShouldRetireAdoPseudoSource(_reconciler.Statuses))
        {
            var retired = _reconciler.RetireWhere(n => n == SourceRetirement.AdoPseudoSource);
            if (retired.Removed.Count > 0) ProcessDiff(retired);
        }
    }

    void ProcessDiff(InboxDiff diff)
    {
        var cfg = _config.Current;
        var now = DateTimeOffset.Now;
        IReadOnlyList<InboxItem> woken;
        lock (_filterGate)
        {
            var result = Snoozes.Evaluate(_snoozes, diff, _reconciler.Entries, now);
            _snoozes = result.Kept.ToList();
            woken = result.Woken;
            _mutes = Snoozes.PruneMutes(_mutes, now).ToList();
        }
        var view = CurrentView();
        var plan = NotificationPlanner.Plan(diff, woken, view, cfg, WorkHours.IsWithin(cfg.WorkHours, DateTime.Now));
        foreach (var t in plan.Toasts) _toasts.Apply(t);
        if (plan.Sound is { } sound) _runtime.Dispatcher.Post(() => _platform.Sound.Play(sound));
        // Hidden items fire nothing: no toast or sound (the planner), no auto-agent (MaybeAutoRun) and no hooks.
        bool Hidden(InboxItem i) => InboxView.IsHidden(i, cfg.Filters);
        foreach (var i in diff.InitialLoad) { _agents.OnItemAdded(i); MaybeAutoRun(i, cfg, view); }
        foreach (var i in diff.Added)
        {
            _agents.OnItemAdded(i);
            if (!Hidden(i)) _ = _hooks.DispatchAsync(HookEvent.ItemAdded, i);
            MaybeAutoRun(i, cfg, view);
        }
        foreach (var c in diff.Changed)
        {
            _agents.OnItemChanged(c);
            if (!Hidden(c.New)) _ = _hooks.DispatchAsync(HookEvent.ItemChanged, c.New);
        }
        foreach (var r in diff.Removed)
        {
            _agents.OnItemRemoved(r);
            if (!Hidden(r)) _ = _hooks.DispatchAsync(HookEvent.ItemRemoved, r);
        }
        PruneRowStates(diff);
        _ = SweepAsync();
        Save();
        Changed?.Invoke();
    }

    void MaybeAutoRun(InboxItem item, AppConfig cfg, InboxView view)
    {
        if (view.AttentionFor(item.Key) != Attention.Normal) return;
        var d = RulesEngine.Decide(item, cfg);
        if (d.Mode == AgentMode.Auto) _agents.TryStart(item, d, manual: false);
    }

    async Task SweepAsync()
    {
        try { await _agents.SweepAsync(_cts.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Log.Error("agent sweep failed", ex); }
    }

    void OnRunChanged(AgentRun run)
    {
        try
        {
            run = _agents.Get(run.Key) ?? run; // RunChanged is a signal only; re-read the latest value (events may arrive out of order)
            // RunChanged also fires for later edits (opened, item removed); announce each finished run once.
            // A run that finishes after its item was hidden is recorded as announced but not shown (no toast, no AgentFinished hook),
            // so unhiding it later can't announce a stale completion.
            if (run.State is AgentState.Ready or AgentState.Failed && run.FinishedAt is { } finished &&
                Find(run.Key) is { } item && TryMarkAnnounced(run.Key, finished) && !InboxView.IsHidden(item, _config.Current.Filters))
            {
                var label = InboxPresenter.LabelFor(run.Profile);
                if (WorkHours.IsWithin(_config.Current.WorkHours, DateTime.Now))
                    _toasts.Apply(new ToastRequest(ToastAction.Show, ItemTag.ForAgent(run.Key),
                        $"{label} {(run.State == AgentState.Ready ? "ready" : "failed")}: {ItemText.RepoAndNumber(item)}",
                        $"{item.Title}\n{run.Summary ?? run.Error}", item.Url));
                _ = _hooks.DispatchAsync(HookEvent.AgentFinished, item, run);
            }
            Save();
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error($"run-changed handler failed for {run.Key}", ex);
        }
    }

    bool TryMarkAnnounced(string key, DateTimeOffset finished)
    {
        lock (_announced)
        {
            if (_announced.TryGetValue(key, out var prev) && prev == finished) return false;
            _announced[key] = finished;
            return true;
        }
    }

    InboxItem? Find(string key) => _reconciler.Entries.FirstOrDefault(e => e.Item.Key == key)?.Item;

    InboxEntry? FindEntry(string key) => _reconciler.Entries.FirstOrDefault(e => e.Item.Key == key);

    /// <summary>
    /// Drops UI state for rows that left the inbox. A Done row whose item is still reported by this poll is hidden
    /// (until the item changes or leaves) so it can't be acted on twice while the provider catches up.
    /// </summary>
    void PruneRowStates(InboxDiff diff)
    {
        var present = _reconciler.Entries.Select(e => e.Item.Key).ToHashSet();
        foreach (var c in diff.Changed) _doneHidden.TryRemove(c.New.Key, out _);
        foreach (var key in _doneHidden.Keys)
            if (!present.Contains(key)) _doneHidden.TryRemove(key, out _);
        foreach (var (key, state) in _rowStates)
        {
            if (!present.Contains(key)) _rowStates.TryRemove(key, out _);
            else if (RowStateMachine.HidesOnNextPoll(state) && _rowStates.TryRemove(new KeyValuePair<string, RowUiState>(key, state)))
                _doneHidden[key] = 0;
            else if (RowStateMachine.ClearsOnNextPoll(state))
                _rowStates.TryRemove(new KeyValuePair<string, RowUiState>(key, state));
        }
    }

    InboxView CurrentView()
    {
        var entries = _doneHidden.IsEmpty ? _reconciler.Entries : _reconciler.Entries.Where(e => !_doneHidden.ContainsKey(e.Item.Key)).ToList();
        TimedMute[] mutes;
        Snooze[] snoozes;
        lock (_filterGate)
        {
            mutes = _mutes.ToArray();
            snoozes = _snoozes.ToArray();
        }
        return InboxView.Compose(entries, _config.Current.Filters, mutes, snoozes, DateTimeOffset.Now);
    }

    public IReadOnlyList<InboxSection> BuildSections(string? query)
    {
        var searching = !string.IsNullOrWhiteSpace(query);
        var sections = InboxFilter.Apply(Present(searching), query);
        if (searching) sections = sections.Select(s => s.BatchButtons ? s with { BatchButtons = false } : s).ToList();
        if (!Batch.Active) return sections;
        return BatchApproval.Lock(sections, key => Find(key)?.Kind == ItemKind.ApprovalPending, BatchBusy);
    }

    public const string BatchBusy = "Finish the batch first";

    /// <summary>The presented sections; while searching, Muted, Waiting and Snoozed are built expanded so their rows can be matched.</summary>
    IReadOnlyList<InboxSection> Present(bool expandAll)
    {
        var cfg = _config.Current;
        bool waitingExpanded, snoozedExpanded, mutedExpanded;
        HashSet<string>? collapsed;
        lock (_filterGate)
        {
            waitingExpanded = _waitingExpanded || expandAll;
            snoozedExpanded = _snoozedExpanded || expandAll;
            mutedExpanded = _mutedExpanded || expandAll;
            collapsed = expandAll ? null : new HashSet<string>(_collapsed);
        }
        return InboxPresenter.Build(CurrentView(), _agents.Get, i => RulesEngine.Decide(i, cfg), _agents.WhyCannotRun,
            DateTimeOffset.Now, _actions.ActionsFor, k => _rowStates.GetValueOrDefault(k),
            r => RepoNames.Display(r, cfg.Display.RepoAliases, PopupSize.RepoChars(cfg.Display.MaxRepoChars, _popupWidth)), waitingExpanded, snoozedExpanded, mutedExpanded, collapsed);
    }

    public IReadOnlyList<SourceStatus> Sources() => _reconciler.Statuses;
    public int HeaderCount => CurrentView().BadgeCount;

    public string? ConfigError => _config.Error;

    public ThemeMode ThemeMode => _themeMode;

    const string StaleMessage = "Source is unavailable — can't act right now";

    public void OnPopupShown()
    {
        var now = DateTimeOffset.Now;
        foreach (var (key, s) in _rowStates)
            if (s.Mode == RowMode.Confirming) _rowStates.TryUpdate(key, RowStateMachine.Rearm(s, now), s);
        Batch.Rearm();
        _justHidden = null; // Undo is offered until the popup is closed and opened again.
    }

    public void CycleTheme()
    {
        _themeMode = ThemeResolver.Next(_themeMode);
        Save();
        ThemeModeChanged?.Invoke();
        Changed?.Invoke();
    }

    public void ToggleExpand(string key)
    {
        var next = RowStateMachine.Toggle(_rowStates.GetValueOrDefault(key));
        if (next.Mode == RowMode.Expanded) CollapseOthers(key);
        if (next.Mode == RowMode.Collapsed) _rowStates.TryRemove(key, out _);
        else _rowStates[key] = next;
        Changed?.Invoke();
    }

    /// <summary>Only one row is open at a time; a row that is sending is never touched, and a Done row is hidden (as on Close).</summary>
    void CollapseOthers(string key)
    {
        foreach (var (k, s) in _rowStates)
            if (k != key && s.Mode != RowMode.Sending && _rowStates.TryRemove(new KeyValuePair<string, RowUiState>(k, s)) && RowStateMachine.HidesItem(s))
                _doneHidden[k] = 0;
    }

    public async Task BeginActionAsync(string key, ItemAction action)
    {
        if (FindEntry(key) is not { Stale: false } entry || !_actions.ActionsFor(entry.Item).Contains(action)) return;
        if (_rowStates.GetValueOrDefault(key) is { Mode: RowMode.Sending or RowMode.Done }) return;
        if (Batch.Active && entry.Item.Kind == ItemKind.ApprovalPending) return; // the popup says "Finish the batch first"
        var begun = RowStateMachine.Begin(action, DateTimeOffset.Now);
        CollapseOthers(key);
        _rowStates[key] = begun;
        Changed?.Invoke();

        ActionPreview preview;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        timeout.CancelAfter(PreviewTimeout);
        try
        {
            preview = await _actions.PreviewAsync(entry.Item, action, _agents.Get(key), timeout.Token);
        }
        catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
        {
            preview = ActionText.Refuse("Timed out checking the current state — try again.");
        }
        catch (Exception ex)
        {
            Log.Error($"action preview {action} {key} failed", ex);
            preview = ActionText.Refuse(ex.Message);
        }
        // The panel starts on the configured merge method when the repo allows it (providers preselect without knowing the config).
        if (preview.Options is { } options)
        {
            var picked = MergeDefaults.Pick(options.Allowed, _config.Current.Actions.MergeMethod);
            preview = preview with { Statement = MergeStatement.WithMethod(preview.Statement, picked), Options = options with { Selected = picked } };
        }
        // Only apply if the row is still in this confirm session (not cancelled or restarted meanwhile).
        _rowStates.TryUpdate(key, RowStateMachine.WithPreview(begun, preview, DateTimeOffset.Now), begun);
        Changed?.Invoke();
    }

    /// <summary>The batch approve/reject panel (v1.3 spec §5).</summary>
    public BatchCoordinator Batch { get; }

    public async Task<string?> BeginBatchAsync(ItemAction action)
    {
        if (action is not (ItemAction.Approve or ItemAction.Reject)) return null;
        if (Batch.Active) return BatchBusy;
        var section = Present(false).FirstOrDefault(s => s.Id == BatchApproval.SectionId);
        if (section is null) return null;
        var rows = BatchApproval.Eligible(section).Where(r => r.Mode != RowMode.Done).ToList();
        if (rows.Any(r => r.Mode == RowMode.Sending)) return "An approval is being sent — wait a moment";
        if (rows.Count < BatchApproval.MinItems) return null;
        foreach (var r in rows.Where(r => r.Mode is RowMode.Confirming or RowMode.Failed)) CancelAction(r.Key);
        var entries = rows.Select(r => Find(r.Key) is { } item ? new BatchEntry(item, _agents.Get(r.Key), r.RepoDisplay) : null)
            .OfType<BatchEntry>().ToList();
        await Batch.OpenAsync(action, entries, _cts.Token);
        return null;
    }

    public void CancelBatch() => Batch.Close();

    public Task ConfirmBatchAsync(string? comment) => Batch.ConfirmAsync(comment, _cts.Token, RefreshAsync);

    public void CancelAction(string key)
    {
        if (_rowStates.GetValueOrDefault(key) is not { Mode: RowMode.Confirming or RowMode.Failed or RowMode.Done } s) return;
        // Closing a Done row (e.g. one kept for its warning) hides it like a plain success, until the item changes or leaves.
        if (s.Mode == RowMode.Done)
        {
            if (_rowStates.TryRemove(new KeyValuePair<string, RowUiState>(key, s)) && RowStateMachine.HidesItem(s)) _doneHidden[key] = 0;
        }
        else _rowStates.TryUpdate(key, RowStateMachine.Cancel(s), s);
        Changed?.Invoke();
    }

    public async Task ConfirmActionAsync(string key, ActionInput input)
    {
        // Defence in depth: the button's ConfirmEnabled is not trusted; re-check the guard here.
        if (_rowStates.GetValueOrDefault(key) is not { } state || !RowStateMachine.CanConfirm(state, DateTimeOffset.Now)) return;
        if (FindEntry(key) is not { } entry) return;
        if (entry.Stale)
        {
            _rowStates.TryUpdate(key, state with { Message = StaleMessage }, state);
            Changed?.Invoke();
            return;
        }
        var sending = RowStateMachine.Sending(state);
        if (!_rowStates.TryUpdate(key, sending, state)) return; // a concurrent confirm already moved this row on
        Changed?.Invoke();

        // Merge and re-run take no comment; only merge takes a method and a branch choice.
        var action = state.Action!.Value;
        var comment = action is ItemAction.Merge or ItemAction.RerunFailed || string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim();
        input = action == ItemAction.Merge
            ? new ActionInput(null, input.Method ?? state.Preview!.Options?.Selected, input.DeleteBranch && state.Preview!.Options is { CanDeleteBranch: true })
            : new ActionInput(comment);
        ActionResult result;
        try
        {
            result = await _actions.ExecuteAsync(entry.Item, action, state.Preview!, input, _cts.Token);
        }
        catch (Exception ex)
        {
            Log.Error($"action {state.Action} {key}: failed (comment {comment?.Length ?? 0} chars)", ex);
            result = new ActionResult(false, ex.Message);
        }
        _rowStates.TryUpdate(key, RowStateMachine.Finished(sending, result), sending);
        // The success refresh usually removes the row (and its inline warning), so a warning is also toasted — regardless of
        // work hours, since the user just acted.
        if (RowStateMachine.ToastFor(result, state.Action!.Value) is { } warning)
            _toasts.Apply(new ToastRequest(ToastAction.Show, ItemTag.For("action:" + key), entry.Item.Repo,
                $"{warning}\n{entry.Item.Title}", entry.Item.Url));
        Changed?.Invoke();
        if (result.Ok || result.Stale) _ = RefreshAsync();
    }

    public void OpenConfig() => OpenPath(ConfigPath);

    public void OpenLogs() => OpenPath(_runtime.Dirs.Logs);

    public void Quit() => _runtime.Quit();

    public void CopyText(string text)
    {
        try { _runtime.CopyText(text); }
        catch (Exception ex) { Log.Error("could not copy to clipboard", ex); }
    }

    public string? TrayProblem()
    {
        if (_config.Error is not null) return _config.Error;
        var s = _reconciler.Statuses.FirstOrDefault(x => !x.Healthy);
        return s is null ? null : $"{s.Name}: {s.Problem}";
    }

    public (int Count, bool Unhealthy) TrayState() =>
        (CurrentView().BadgeCount, _config.Error is not null || _reconciler.Statuses.Any(s => !s.Healthy));

    public void SnoozeItem(string key, SnoozeChoice choice)
    {
        var (until, untilChange) = SnoozeTimes.For(choice, DateTimeOffset.Now, _config.Current.WorkHours);
        lock (_filterGate)
        {
            _snoozes.RemoveAll(s => s.Key == key);
            _snoozes.Add(new Snooze(key, until, untilChange));
        }
        _toasts.Apply(new ToastRequest(ToastAction.Remove, ItemTag.For(key)));
        _toasts.Apply(new ToastRequest(ToastAction.Remove, ItemTag.ForAgent(key)));
        _rowStates.TryRemove(key, out _);
        Save();
        Changed?.Invoke();
    }

    public void Unsnooze(string key)
    {
        lock (_filterGate) _snoozes.RemoveAll(s => s.Key == key);
        Save();
        Changed?.Invoke();
    }

    public void Mute(string key, MuteScope scope, MuteDuration duration)
    {
        if (Find(key) is not { } item || MutePatterns.For(item, scope) is not { } pattern) return;
        if (duration == MuteDuration.Forever)
        {
            if (!EditMuteConfig(list => { if (!list.Contains(pattern, StringComparer.OrdinalIgnoreCase)) list.Add(pattern); })) return;
        }
        else
        {
            var until = MutePatterns.Until(duration, DateTimeOffset.Now, _config.Current.WorkHours);
            lock (_filterGate) _mutes.Add(new TimedMute(pattern, until));
        }
        Save();
        Changed?.Invoke();
    }

    public IReadOnlyList<ActiveMute> ActiveMutes()
    {
        var now = DateTimeOffset.Now;
        List<TimedMute> timed;
        lock (_filterGate) timed = _mutes.ToList();
        return _config.Current.Filters.Mute.Select(p => new ActiveMute(p, "forever", true))
            .Concat(timed.Where(m => m.Until is { } u && u > now).Select(m => new ActiveMute(m.Pattern, Snoozes.Left(m.Until!.Value - now, m.Until.Value), false)))
            .ToList();
    }

    public void Unmute(string pattern)
    {
        lock (_filterGate) _mutes.RemoveAll(m => string.Equals(m.Pattern, pattern, StringComparison.OrdinalIgnoreCase));
        if (_config.Current.Filters.Mute.Contains(pattern, StringComparer.OrdinalIgnoreCase)
            && !EditMuteConfig(list => list.RemoveAll(p => string.Equals(p, pattern, StringComparison.OrdinalIgnoreCase)))) return;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Adds a filters.hide pattern for the item's repo, project or org. The footer offers Undo until the popup is reopened.</summary>
    public void HideMatching(string key, MuteScope scope)
    {
        if (Find(key) is not { } item || MutePatterns.For(item, scope) is not { } pattern) return;
        var added = false;
        if (!EditMuteConfig(list =>
            {
                if (list.Contains(pattern, StringComparer.OrdinalIgnoreCase)) return;
                list.Add(pattern);
                added = true;
            }, FilterList.Hide)) return;
        Log.Info($"hid {pattern}");
        // Undo only removes a pattern this click added, never one that was already in Settings.
        if (added) _justHidden = pattern;
        // Like a snooze: clear the hidden items' toasts from Action Center so none can be clicked through.
        foreach (var e in _reconciler.Entries.Where(e => InboxView.IsHidden(e.Item, _config.Current.Filters)))
        {
            _toasts.Apply(new ToastRequest(ToastAction.Remove, ItemTag.For(e.Item.Key)));
            _toasts.Apply(new ToastRequest(ToastAction.Remove, ItemTag.ForAgent(e.Item.Key)));
            // Keep Sending and Done: an action in flight must still finish into its own state (and Undo must not re-offer it).
            if (_rowStates.TryGetValue(e.Item.Key, out var s) && s.Mode is not (RowMode.Sending or RowMode.Done))
                _rowStates.TryRemove(KeyValuePair.Create(e.Item.Key, s));
        }
        // An open batch must not go on to send items that are now hidden; a running one can't be stopped and finishes.
        if (Batch.Phase is BatchPhase.Previewing or BatchPhase.Ready && Batch.Lines.Any(l => InboxView.IsHidden(l.Item, _config.Current.Filters)))
            Batch.Close();
        Changed?.Invoke();
    }

    public string? JustHidden => _justHidden;

    public void Unhide(string pattern)
    {
        // Already gone (removed in Settings meanwhile): nothing to save.
        if (!_config.Current.Filters.Hide.Contains(pattern, StringComparer.OrdinalIgnoreCase))
        {
            _justHidden = null;
            Changed?.Invoke();
            return;
        }
        if (!EditMuteConfig(list => list.RemoveAll(p => string.Equals(p, pattern, StringComparison.OrdinalIgnoreCase)), FilterList.Hide)) return;
        if (string.Equals(_justHidden, pattern, StringComparison.OrdinalIgnoreCase)) _justHidden = null;
        Changed?.Invoke();
    }

    /// <summary>Every filters.hide pattern with how many current items it hides.</summary>
    public IReadOnlyList<HiddenFilter> HiddenFilters()
    {
        var items = _reconciler.Entries.Select(e => e.Item).ToList();
        return _config.Current.Filters.Hide.Select(p => new HiddenFilter(p, Math.Max(0, SettingsDraft.CountMatches(p, items)))).ToList();
    }

    /// <summary>Applies an edit to filters.mute (or filters.hide) in config.json with the Settings guards; false (after a toast) when it was not saved.</summary>
    bool EditMuteConfig(Action<List<string>> edit, FilterList list = FilterList.Mute)
    {
        var result = MuteConfigEditor.TrySave(_config, edit, _commentsWarningShown, list);
        if (result == MuteSaveResult.Saved) return true;
        var what = list == FilterList.Hide ? "hide" : "mute";
        var message = result == MuteSaveResult.CommentsNotAcknowledged
            ? $"Couldn't save {what}: config.json has comments — use Settings to save it once"
            : $"Couldn't save {what}: config.json changed or has an error. Fix it or use Settings.";
        _toasts.Apply(new ToastRequest(ToastAction.Show, ItemTag.For("mute-error"), "gh_notify", message));
        return false;
    }

    public void ToggleSection(string id)
    {
        lock (_filterGate)
        {
            if (id == "waiting") _waitingExpanded = !_waitingExpanded;
            else if (id == "snoozed") _snoozedExpanded = !_snoozedExpanded;
            else if (id == InboxPresenter.MutedSectionId) _mutedExpanded = !_mutedExpanded;
            else if (!_collapsed.Remove(id)) _collapsed.Add(id);
        }
        Save();
        Changed?.Invoke();
    }

    public void RunAgent(string key)
    {
        if (Find(key) is not { } item) return;
        _agents.TryStart(item, RulesEngine.Decide(item, _config.Current), manual: true);
    }

    public void OpenInClaude(string key)
    {
        if (_agents.Get(key) is not { SessionId: { } sid, WorktreePath: { } wt }) return;
        try
        {
            _launcher.Resume(wt, sid);
            _agents.MarkOpened(key);
        }
        catch (Exception ex)
        {
            Log.Error($"could not open claude for {key}", ex);
        }
    }

    /// <summary>
    /// A toast click. A finished agent run (e.g. "Pre-review ready") resumes its Claude session, where the review continues;
    /// other toasts, and a run whose session or worktree is gone, open their URL. False when the click should just open the popup.
    /// </summary>
    public bool OnToastActivated(string tag, string action)
    {
        var entry = _reconciler.Entries.FirstOrDefault(e => ItemTag.ForAgent(e.Item.Key) == tag);
        if (entry is not null && _agents.Get(entry.Item.Key) is { State: AgentState.Ready, SessionId: not null, WorktreePath: { } wt } &&
            Directory.Exists(wt))
        {
            OpenInClaude(entry.Item.Key);
            return true;
        }
        if (!ToastActions.TryGetUrl(action, out var url)) return false;
        OpenUrl(url);
        return true;
    }

    /// <summary>Opens an item URL (row click, toast). Item URLs come from API data, so only https is shell-executed.</summary>
    public void OpenUrl(string url)
    {
        if (!ItemUrl.IsOpenable(url))
        {
            Log.Error($"refusing to open non-https item URL: {url}");
            return;
        }
        ShellOpen(() => _platform.Shell.OpenUrl(url), url);
    }

    /// <summary>Opens one of our own local paths (config file, logs folder).</summary>
    public void OpenPath(string path) => ShellOpen(() => _platform.Shell.OpenFolder(path), path);

    public void OpenSettings() => SettingsRequested?.Invoke();

    string? _lastRunVersion;
    string? _whatsNewVersion;
    readonly bool _usedBefore;

    public string? WhatsNewVersion => _whatsNewVersion;

    /// <summary>Opens this version's release notes (tray menu or footer link) and clears the footer link.</summary>
    public void OpenWhatsNew()
    {
        OpenUrl(WhatsNew.UrlFor(Version));
        DismissWhatsNew();
    }

    public void DismissWhatsNew()
    {
        if (_whatsNewVersion is null) return;
        _whatsNewVersion = null;
        Save();
        Changed?.Invoke();
    }

    // ----- ISettingsHost -----

    public AppConfig CurrentConfig => _config.Current;
    public string LogsPath => _runtime.Dirs.Logs;
    public string DataPath => _runtime.Dirs.DataDir;
    public string? ConfigFileHash() => _config.LoadedHash;

    /// <summary>Settings only lifts timed mutes; permanent ones are edited in the draft's filters.mute and saved with it.</summary>
    void ISettingsHost.Unmute(string pattern)
    {
        lock (_filterGate) _mutes.RemoveAll(m => string.Equals(m.Pattern, pattern, StringComparison.OrdinalIgnoreCase));
        Save();
        Changed?.Invoke();
    }

    /// <summary>Saves through the store (validation, conflict check, .bak), then applies it like a file-watcher reload.</summary>
    public void SaveConfig(AppConfig config, string? expectedHash)
    {
        _config.Save(config, expectedHash);
        AfterConfigChanged();
    }

    public IReadOnlyList<string> DiscoveredAdoOrgs() =>
        _reconciler.Statuses.Where(s => s.Name.StartsWith("ADO:", StringComparison.Ordinal)).Select(s => s.Name["ADO:".Length..]).ToList();

    public async Task<string> TestAdoSignInAsync(string? subscription, string? tenant)
    {
        var provider = new AzTokenProvider(new ProcessRunner(), config: () => new AdoConfig { AzSubscription = subscription, AzTenant = tenant });
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            await provider.GetTokenAsync(forceRefresh: true, timeout.Token);
            return "✓ Signed in";
        }
        catch (OperationCanceledException) { return "Timed out waiting for az"; }
        catch (Exception ex) { return ex.Message; }
    }

    public bool IsSkillInstalled(string skill) => _skills.IsInstalled(skill, null);

    public IReadOnlyList<InboxItem> CurrentItems() => _reconciler.Entries.Select(e => e.Item).ToList();

    public UpdateStatus UpdateStatus => Update;

    public bool StartWithWindows
    {
        get => _platform.Startup.IsEnabled;
        set => _platform.Startup.SetEnabled(value);
    }

    public string? StartupProblem => _platform.Startup.Problem;

    public void SetThemeMode(ThemeMode mode)
    {
        if (mode == _themeMode) return;
        _themeMode = mode;
        Save();
        ThemeModeChanged?.Invoke();
        Changed?.Invoke();
    }

    public double PopupWidth => _popupWidth;
    public double TextScale => _textScale;

    public void SetPopupWidth(double width)
    {
        width = PopupSize.ClampWidth(width);
        if (width == _popupWidth) return;
        _popupWidth = width;
        Save();
        Changed?.Invoke(); // longer repo names fit
    }

    public void SetTextScale(double scale)
    {
        scale = PopupSize.NormalizeScale(scale);
        if (scale == _textScale) return;
        _textScale = scale;
        Save();
        Changed?.Invoke();
    }

    public void PlaySound(string sound) => _platform.Sound.Play(sound);

    public bool CommentsWarningShown
    {
        get => _commentsWarningShown;
        set
        {
            _commentsWarningShown = value;
            Save();
        }
    }

    static void ShellOpen(Action open, string target)
    {
        try { open(); }
        catch (Exception ex) { Log.Error($"could not open {target}", ex); }
    }

    void Save()
    {
        lock (_saveGate)
        {
            try
            {
                AppState state;
                lock (_filterGate)
                {
                    state = new AppState
                    {
                        Snapshot = _reconciler.Snapshot(),
                        AgentRuns = _agents.Runs.Values.ToList(),
                        ThemeMode = _themeMode,
                        Mutes = _mutes.ToList(),
                        Snoozes = _snoozes.ToList(),
                        WaitingExpanded = _waitingExpanded,
                        SnoozedExpanded = _snoozedExpanded,
                        MutedExpanded = _mutedExpanded,
                        CollapsedSections = _collapsed.Order(StringComparer.Ordinal).ToList(),
                        CommentsWarningShown = _commentsWarningShown,
                        LastRunVersion = _lastRunVersion,
                        WhatsNewVersion = _whatsNewVersion,
                        PopupWidth = _popupWidth == PopupSize.DefaultWidth ? null : _popupWidth,
                        TextScale = _textScale,
                    };
                }
                _stateStore.Save(state);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error("could not save state", ex);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _agents.Dispose();
        try { _agents.WhenIdle().Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
        Save();
    }
}
