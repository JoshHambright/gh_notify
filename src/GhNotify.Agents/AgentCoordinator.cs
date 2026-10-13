using System.Collections.Concurrent;
using GhNotify.Core;

namespace GhNotify.Agents;

public sealed record AgentDeps(
    ILocalRepoIndex Repos, IWorktreeManager Worktrees, IClaudeRunner Claude, ISkillLocator Skills, Func<bool> ClaudeAvailable);

public sealed class AgentCoordinator : IDisposable
{
    static readonly TimeSpan Keep = TimeSpan.FromHours(24);
    readonly AgentDeps _d;
    readonly Func<AppConfig> _config;
    readonly string _scratch;
    readonly TimeProvider _time;
    readonly object _gate = new();
    readonly ConcurrentDictionary<string, AgentRun> _runs = new();
    readonly ConcurrentDictionary<string, string?> _currentHead = new();
    readonly ConcurrentDictionary<string, Task> _inflight = new();
    readonly SemaphoreSlim _slots;
    readonly CancellationTokenSource _shutdown = new();

    public AgentCoordinator(AgentDeps deps, Func<AppConfig> config, string scratchDir, TimeProvider? time = null, IEnumerable<AgentRun>? restored = null)
    {
        _d = deps;
        _config = config;
        _scratch = scratchDir;
        _time = time ?? TimeProvider.System;
        _slots = new SemaphoreSlim(Math.Max(1, config().MaxConcurrentAgents));
        foreach (var r in restored ?? [])
            _runs[r.Key] = r.State is AgentState.Queued or AgentState.Running
                ? r with { State = AgentState.Failed, Error = "Interrupted by app restart" }
                : r;
    }

    public event Action<AgentRun>? RunChanged;
    public event Action<string>? RunRemoved;

    public IReadOnlyDictionary<string, AgentRun> Runs => _runs;
    public AgentRun? Get(string key) => _runs.GetValueOrDefault(key);
    DateTimeOffset Now => _time.GetUtcNow();

    public string? WhyCannotRun(InboxItem item, AgentDecision decision)
    {
        if (decision.Profile is not { } profile) return "No agent profile for this item";
        if (!_d.ClaudeAvailable()) return "claude CLI not found on PATH";
        string? clone = null;
        if (profile.NeedsWorktree)
        {
            if (item.CloneUrl is null || item.SourceRef is null) return "This item has no branch to check out";
            clone = _d.Repos.FindClone(item.CloneUrl);
            if (clone is null) return "No local clone under repoRoots";
        }
        if (profile.RequiresSkill is { } skill && !_d.Skills.IsInstalled(skill, clone)) return $"Skill /{skill} not installed";
        return null;
    }

    public bool TryStart(InboxItem item, AgentDecision decision, bool manual)
    {
        if (WhyCannotRun(item, decision) is not null) return false;
        var profile = decision.Profile!;
        AgentRun run;
        lock (_gate)
        {
            var existing = Get(item.Key);
            if (existing is { State: AgentState.Queued or AgentState.Running }) return false;
            if (!manual && existing is not null) return false;
            run = new AgentRun
            {
                Key = item.Key,
                Profile = decision.ProfileName!,
                State = AgentState.Queued,
                HeadSha = item.HeadSha,
                ClonePath = profile.NeedsWorktree ? _d.Repos.FindClone(item.CloneUrl!) : null,
                LastOpenedAt = existing?.LastOpenedAt,
            };
            _currentHead[item.Key] = item.HeadSha;
            _runs[item.Key] = run;
        }
        RunChanged?.Invoke(run); // raise Queued before the worker can raise Running
        _inflight[item.Key] = Task.Run(() => ExecuteAsync(item, profile, run.ClonePath));
        return true;
    }

    async Task ExecuteAsync(InboxItem item, AgentProfile profile, string? clone)
    {
        var ct = _shutdown.Token;
        try
        {
            await _slots.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        try
        {
            Update(item.Key, r => r with { State = AgentState.Running, StartedAt = Now });
            var tag = ItemTag.For(item.Key);
            string cwd;
            if (profile.NeedsWorktree)
            {
                cwd = await _d.Worktrees.CreateAsync(clone!, item.SourceRef!, tag, ct);
            }
            else
            {
                cwd = Path.Combine(_scratch, tag);
                Directory.CreateDirectory(cwd);
            }
            Update(item.Key, r => r with { WorktreePath = cwd });
            var cfg = _config();
            var o = await _d.Claude.RunAsync(cwd, PromptTemplate.Fill(profile.Prompt, item), profile, cfg.AgentAllowedTools,
                TimeSpan.FromMinutes(cfg.AgentTimeoutMinutes), ct);
            Update(item.Key, r => r with
            {
                State = !o.Success ? AgentState.Failed
                    : _currentHead.GetValueOrDefault(item.Key) == r.HeadSha ? AgentState.Ready : AgentState.Outdated,
                SessionId = o.SessionId,
                Summary = o.Summary,
                CostUsd = o.CostUsd,
                Error = o.Error,
                FinishedAt = Now,
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Log.Error($"agent run for {item.Key} failed", ex);
            Update(item.Key, r => r with { State = AgentState.Failed, Error = ex.Message, FinishedAt = Now });
        }
        finally
        {
            _slots.Release();
            _inflight.TryRemove(item.Key, out _);
        }
    }

    public void OnItemAdded(InboxItem item)
    {
        _currentHead[item.Key] = item.HeadSha;
        if (Get(item.Key) is null) return;
        Update(item.Key, r => r with
        {
            ItemRemovedAt = null,
            State = r.State is AgentState.Ready or AgentState.Failed && r.HeadSha != item.HeadSha ? AgentState.Outdated : r.State,
        });
    }

    public void OnItemChanged(ItemChange change)
    {
        _currentHead[change.New.Key] = change.New.HeadSha;
        if (change.HeadChanged && Get(change.New.Key) is { State: AgentState.Ready or AgentState.Failed })
            Update(change.New.Key, r => r with { State = AgentState.Outdated });
    }

    public void OnItemRemoved(InboxItem item)
    {
        if (Get(item.Key) is { ItemRemovedAt: null })
            Update(item.Key, r => r with { ItemRemovedAt = Now });
    }

    public void MarkOpened(string key)
    {
        if (Get(key) is not null) Update(key, r => r with { LastOpenedAt = Now });
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        foreach (var r in _runs.Values.ToList())
        {
            if (r.ItemRemovedAt is not { } removed || r.State is AgentState.Queued or AgentState.Running) continue;
            var openedRecently = r.LastOpenedAt is { } opened && Now - opened < Keep;
            if (openedRecently && Now - removed < Keep) continue;
            if (r.WorktreePath is { } wt)
            {
                try
                {
                    if (r.ClonePath is { } clone) await _d.Worktrees.RemoveAsync(clone, wt, ItemTag.For(r.Key), ct);
                    else if (Directory.Exists(wt)) Directory.Delete(wt, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Error($"could not clean {wt}", ex);
                }
            }
            if (_runs.TryRemove(r.Key, out _)) RunRemoved?.Invoke(r.Key);
        }
    }

    public Task WhenIdle() => Task.WhenAll(_inflight.Values.ToList());

    public void Dispose() => _shutdown.Cancel();

    void Update(string key, Func<AgentRun, AgentRun> change)
    {
        AgentRun next;
        lock (_gate)
        {
            if (!_runs.TryGetValue(key, out var current)) return;
            next = change(current);
            _runs[key] = next;
        }
        RunChanged?.Invoke(next);
    }
}
