using System.Diagnostics;
using GhNotify.Ado;
using GhNotify.Agents;
using GhNotify.Core;
using GhNotify.GitHub;

namespace GhNotify.App;

public sealed class AppController : IDisposable
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
    bool _claudeAvailable;

    public event Action? Changed;

    public AppController(ConfigStore config, StateStore stateStore)
    {
        _config = config;
        _stateStore = stateStore;
        var state = stateStore.Load();
        _reconciler = new Reconciler(state.Snapshot.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<InboxItem>)kv.Value));

        var runner = new ProcessRunner();
        var ghSource = new GitHubSource(
            new GitHubClient(new HttpClient { Timeout = TimeSpan.FromSeconds(25) }, new GhTokenProvider(runner)),
            () => _config.Current.GitHub,
            () => _reconciler.ItemsFor("GitHub"));
        var adoClient = new AdoClient(new HttpClient { Timeout = TimeSpan.FromSeconds(25) }, new AzTokenProvider(runner));
        var directory = new AdoDirectory(adoClient);
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
            new SkillLocator(SkillLocator.DefaultClaudeHome), () => _claudeAvailable);
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

    public async Task RefreshAsync()
    {
        try { await _poller.PollOnceAsync(_cts.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Log.Error("manual refresh failed", ex); }
    }

    public void ReloadConfig()
    {
        _config.Load();
        _claudeAvailable = CommandResolver.Find("claude") is not null;
        HandleDiff(_reconciler.RetireWhere(IsDisabledSource));
        Changed?.Invoke();
        _ = RefreshAsync();
    }

    bool IsDisabledSource(string name) => SourceRetirement.IsDisabled(name, _config.Current);

    void HandleDiff(InboxDiff diff)
    {
        ProcessDiff(diff);
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
        foreach (var t in NotificationPolicy.Plan(diff, WorkHours.IsWithin(cfg.WorkHours, DateTime.Now))) _toasts.Apply(t);
        foreach (var i in diff.InitialLoad) { _agents.OnItemAdded(i); MaybeAutoRun(i, cfg); }
        foreach (var i in diff.Added) { _agents.OnItemAdded(i); _ = _hooks.DispatchAsync(HookEvent.ItemAdded, i); MaybeAutoRun(i, cfg); }
        foreach (var c in diff.Changed) { _agents.OnItemChanged(c); _ = _hooks.DispatchAsync(HookEvent.ItemChanged, c.New); }
        foreach (var r in diff.Removed) { _agents.OnItemRemoved(r); _ = _hooks.DispatchAsync(HookEvent.ItemRemoved, r); }
        _ = SweepAsync();
        Save();
        Changed?.Invoke();
    }

    void MaybeAutoRun(InboxItem item, AppConfig cfg)
    {
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
                        $"{label} {(run.State == AgentState.Ready ? "ready" : "failed")}: {item.Repo}",
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

    public IReadOnlyList<InboxSection> BuildSections() =>
        InboxPresenter.Build(_reconciler.Entries, _agents.Get, i => RulesEngine.Decide(i, _config.Current), _agents.WhyCannotRun,
            DateTimeOffset.Now);

    public IReadOnlyList<string> Problems()
    {
        var list = _reconciler.Statuses.Where(s => !s.Healthy).Select(s => $"{s.Name}: {s.Problem}").ToList();
        if (_config.Error is { } e) list.Insert(0, e);
        return list;
    }

    public (int Count, bool Unhealthy) TrayState() =>
        (_reconciler.Entries.Count, _config.Error is not null || _reconciler.Statuses.Any(s => !s.Healthy));

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

    public void OpenUrl(string target)
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
                _stateStore.Save(new AppState { Snapshot = _reconciler.Snapshot(), AgentRuns = _agents.Runs.Values.ToList() });
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
