using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using GhNotify.Ado;
using GhNotify.Agents;
using GhNotify.Core;
using GhNotify.GitHub;
using ThemeMode = GhNotify.Core.ThemeMode;

namespace GhNotify.App;

public sealed class AppController : IPopupHost, ISettingsHost, IDisposable
{
    readonly ConfigStore _config;
    readonly StateStore _stateStore;
    readonly Reconciler _reconciler;
    readonly Poller _poller;
    readonly RepoLocator _repos;
    readonly AgentCoordinator _agents;
    readonly HookDispatcher _hooks;
    readonly ToastPresenter _toasts = new();
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
    bool _commentsWarningShown;
    readonly SkillLocator _skills = new(SkillLocator.DefaultClaudeHome);

    public event Action? Changed;
    /// <summary>The popup asked for the settings window (⋯ → Settings…, or "Fix in Settings"); App opens it.</summary>
    public event Action? SettingsRequested;
    /// <summary>Raised (on the caller's thread) after <see cref="CycleTheme"/> changes the persisted mode.</summary>
    public event Action? ThemeModeChanged;

    UpdateService? _updates;
    public AppConfig Config => _config.Current;
    /// <summary>Attaches the background update service (created after the controller because it reads <see cref="Config"/>).</summary>
    public void AttachUpdates(UpdateService updates) => _updates = updates;
    public void RaiseChanged() => Changed?.Invoke();
    public UpdateStatus Update => _updates?.Status ?? new UpdateStatus(UpdatePhase.Off, null, null, null, null);
    public int RunningAgents => _agents.Runs.Values.Count(r => r.State is AgentState.Running or AgentState.Queued);
    public Task CheckForUpdatesAsync() => _updates?.CheckNowAsync(_cts.Token) ?? Task.CompletedTask;

    public RestartDecision RestartToUpdate(bool confirmed)
    {
        var d = _updates?.RequestRestart(RunningAgents, confirmed) ?? RestartDecision.NothingToApply;
        if (d == RestartDecision.Restarting) Application.Current.Shutdown();
        return d;
    }

    public AppController(ConfigStore config, StateStore stateStore)
    {
        _config = config;
        _stateStore = stateStore;
        var state = stateStore.Load();
        _themeMode = state.ThemeMode;
        _mutes = state.Mutes.ToList();
        _snoozes = state.Snoozes.ToList();
        _waitingExpanded = state.WaitingExpanded;
        _snoozedExpanded = state.SnoozedExpanded;
        _commentsWarningShown = state.CommentsWarningShown;
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
        var catalog = new CompositeCatalog(
            new StaticCatalog(() => _config.Current.GitHub.Enabled ? [ghSource] : []),
            new AdoSourceCatalog(directory, () => _config.Current.Ado,
                org => new AdoOrgSource(org, adoClient, directory, () => _config.Current.Ado,
                    () => _reconciler.ItemsFor($"ADO:{org}"))));
        _poller = new Poller(catalog, _reconciler, TimeSpan.FromSeconds(30));
        _poller.Polled += HandleDiff;

        _repos = new RepoLocator(() => _config.Current.RepoRoots);
        _claudeAvailable = CommandResolver.Find("claude") is not null;
        var deps = new AgentDeps(_repos, new WorktreeManager(runner, AppPaths.Worktrees), new ClaudeRunner(runner),
            new SkillLocator(SkillLocator.DefaultClaudeHome), () => _claudeAvailable, new CommandRunner(runner));
        _agents = new AgentCoordinator(deps, () => _config.Current, AppPaths.Scratch, restored: state.AgentRuns);
        foreach (var r in state.AgentRuns)
            if (r.FinishedAt is { } f) _announced[r.Key] = f; // don't re-announce runs finished before a restart
        _agents.RunChanged += OnRunChanged;
        _agents.RunRemoved += _ => { Save(); Changed?.Invoke(); };
        _hooks = new HookDispatcher(runner, () => _config.Current);
        _launcher = new ClaudeLauncher(runner);
    }

    public DateTimeOffset? LastPolledAt => _poller.LastPolledAt;
    public string ConfigPath => _config.Path;

    public void Start()
    {
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
        _config.Load();
        AfterConfigChanged();
    }

    /// <summary>Applies a newly loaded config: retires disabled sources, re-renders and polls.</summary>
    void AfterConfigChanged()
    {
        _claudeAvailable = CommandResolver.Find("claude") is not null;
        HandleDiff(_reconciler.RetireWhere(IsDisabledSource));
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
        if (plan.Sound is { } sound) Application.Current?.Dispatcher.InvokeAsync(() => SoundPlayerService.PlayOrDefault(sound));
        foreach (var i in diff.InitialLoad) { _agents.OnItemAdded(i); MaybeAutoRun(i, cfg, view); }
        foreach (var i in diff.Added) { _agents.OnItemAdded(i); _ = _hooks.DispatchAsync(HookEvent.ItemAdded, i); MaybeAutoRun(i, cfg, view); }
        foreach (var c in diff.Changed) { _agents.OnItemChanged(c); _ = _hooks.DispatchAsync(HookEvent.ItemChanged, c.New); }
        foreach (var r in diff.Removed) { _agents.OnItemRemoved(r); _ = _hooks.DispatchAsync(HookEvent.ItemRemoved, r); }
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
            if (run.State is AgentState.Ready or AgentState.Failed && run.FinishedAt is { } finished &&
                Find(run.Key) is { } item && TryMarkAnnounced(run.Key, finished))
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

    public IReadOnlyList<InboxSection> BuildSections()
    {
        var cfg = _config.Current;
        bool waitingExpanded, snoozedExpanded;
        lock (_filterGate) { waitingExpanded = _waitingExpanded; snoozedExpanded = _snoozedExpanded; }
        return InboxPresenter.Build(CurrentView(), _agents.Get, i => RulesEngine.Decide(i, cfg), _agents.WhyCannotRun,
            DateTimeOffset.Now, _actions.ActionsFor, k => _rowStates.GetValueOrDefault(k),
            r => RepoNames.Display(r, cfg.Display.RepoAliases, cfg.Display.MaxRepoChars), waitingExpanded, snoozedExpanded);
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
            if (k != key && s.Mode != RowMode.Sending && _rowStates.TryRemove(new KeyValuePair<string, RowUiState>(k, s)) && s.Mode == RowMode.Done)
                _doneHidden[k] = 0;
    }

    public async Task BeginActionAsync(string key, ItemAction action)
    {
        if (FindEntry(key) is not { Stale: false } entry || !_actions.ActionsFor(entry.Item).Contains(action)) return;
        if (_rowStates.GetValueOrDefault(key) is { Mode: RowMode.Sending or RowMode.Done }) return;
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
        // Only apply if the row is still in this confirm session (not cancelled or restarted meanwhile).
        _rowStates.TryUpdate(key, RowStateMachine.WithPreview(begun, preview, DateTimeOffset.Now), begun);
        Changed?.Invoke();
    }

    public void CancelAction(string key)
    {
        if (_rowStates.GetValueOrDefault(key) is not { Mode: RowMode.Confirming or RowMode.Failed or RowMode.Done } s) return;
        // Closing a Done row (e.g. one kept for its warning) hides it like a plain success, until the item changes or leaves.
        if (s.Mode == RowMode.Done)
        {
            if (_rowStates.TryRemove(new KeyValuePair<string, RowUiState>(key, s))) _doneHidden[key] = 0;
        }
        else _rowStates.TryUpdate(key, RowStateMachine.Cancel(s), s);
        Changed?.Invoke();
    }

    public async Task ConfirmActionAsync(string key, string? comment)
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

        comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        ActionResult result;
        try
        {
            result = await _actions.ExecuteAsync(entry.Item, state.Action!.Value, state.Preview!, comment, _cts.Token);
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

    public void OpenLogs() => OpenPath(AppPaths.Logs);

    public void Quit() => Application.Current?.Shutdown();

    public void CopyText(string text)
    {
        try { Clipboard.SetText(text); }
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

    /// <summary>Applies an edit to filters.mute in config.json (overwriting, no conflict check); false when it could not be saved.</summary>
    bool EditMuteConfig(Action<List<string>> edit)
    {
        try
        {
            var next = ConfigStore.Clone(_config.Current);
            edit(next.Filters.Mute);
            _config.Save(next, expectedHash: null);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            Log.Error("could not save mute", ex);
            _toasts.Apply(new ToastRequest(ToastAction.Show, ItemTag.For("mute-error"), "gh_notify", $"Couldn't save mute: {ex.Message}"));
            return false;
        }
    }

    public void ToggleSection(string id)
    {
        lock (_filterGate)
        {
            if (id == "waiting") _waitingExpanded = !_waitingExpanded;
            else if (id == "snoozed") _snoozedExpanded = !_snoozedExpanded;
            else return;
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

    /// <summary>Opens an item URL (row click, toast). Item URLs come from API data, so only https is shell-executed.</summary>
    public void OpenUrl(string url)
    {
        if (!ItemUrl.IsOpenable(url))
        {
            Log.Error($"refusing to open non-https item URL: {url}");
            return;
        }
        ShellOpen(url);
    }

    /// <summary>Opens one of our own local paths (config file, logs folder).</summary>
    public void OpenPath(string path) => ShellOpen(path);

    public void OpenSettings() => SettingsRequested?.Invoke();

    // ----- ISettingsHost -----

    public AppConfig CurrentConfig => _config.Current;
    public string LogsPath => AppPaths.Logs;
    public string DataPath => AppPaths.DataDir;
    public string? ConfigFileHash() => _config.FileHash();

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
        get => StartupRegistration.IsEnabled();
        set => StartupRegistration.Set(value);
    }

    public void SetThemeMode(ThemeMode mode)
    {
        if (mode == _themeMode) return;
        _themeMode = mode;
        Save();
        ThemeModeChanged?.Invoke();
        Changed?.Invoke();
    }

    public bool CommentsWarningShown
    {
        get => _commentsWarningShown;
        set
        {
            _commentsWarningShown = value;
            Save();
        }
    }

    static void ShellOpen(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose(); }
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
                        Snapshot = _reconciler.Snapshot(), AgentRuns = _agents.Runs.Values.ToList(), ThemeMode = _themeMode,
                        Mutes = _mutes.ToList(), Snoozes = _snoozes.ToList(), WaitingExpanded = _waitingExpanded, SnoozedExpanded = _snoozedExpanded,
                        CommentsWarningShown = _commentsWarningShown,
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
