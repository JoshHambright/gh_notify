using GhNotify.Core;

namespace GhNotify.UI;

/// <summary>
/// Fixed, neutral sample data (contoso / fabrikam / acme, alice / bob) for --render-preview. Every command is a no-op:
/// it never touches the network, tokens or the shell.
/// </summary>
public sealed class SampleHost : IPopupHost
{
    public const string ApprovalKey = "ado:approval:contoso/platform/7f1c";
    public const string Approval2Key = "ado:approval:contoso/platform/8a2d";
    public const string ReadyKey = "gh:ready:acme/web#31";
    public const string ReviewKey = "ado:review:fabrikam/customer-platform/checkout-service-api#482";
    public const string Review2Key = "ado:review:fabrikam/customer-platform/web-portal#479";
    public const string CiKey = "gh:ci:acme/api#57";
    public const string SnoozedKey = "gh:review:fabrikam/web#203";
    public const string MutedKey = "gh:review:acme/legacy-billing#12";

    readonly DateTimeOffset _now = DateTimeOffset.Now;
    readonly List<InboxEntry> _entries;
    readonly Dictionary<string, AgentRun> _runs;

    public Dictionary<string, RowUiState> States { get; } = new();
    public bool Empty { get; set; }
    /// <summary>Items left out of the inbox (tests use it to leave a single approval, or to make a row disappear on refresh).</summary>
    public HashSet<string> HiddenKeys { get; } = [];
    /// <summary>No active mutes (the footer menu shows "Nothing muted").</summary>
    public bool NoMutes { get; set; }
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;

    public SampleHost()
    {
        Batch = new BatchCoordinator(new SampleActions(), TimeSpan.FromSeconds(1), () => Clock());
        Batch.Changed += () => Changed?.Invoke();
        var approval = new InboxItem(ApprovalKey, Provider.Ado, ItemKind.ApprovalPending, "contoso/platform",
            "platform-release 20261013.4", "https://dev.azure.com/contoso/platform/_build/results?buildId=1042", "",
            _now.AddMinutes(-12), null, null, "Instructions: check the release notes before approving")
        {
            Org = "contoso",
            Project = "platform",
            ApprovalId = "7f1c",
            Details = DetailList.Of(("Pipeline", "platform-release"), ("Run", "20261013.4"), ("Project", "platform"),
                ("Created", _now.AddMinutes(-12).ToString("yyyy-MM-dd HH:mm"))),
        };
        var approval2 = new InboxItem(Approval2Key, Provider.Ado, ItemKind.ApprovalPending, "contoso/platform",
            "platform-hotfix 20261013.5", "https://dev.azure.com/contoso/platform/_build/results?buildId=1043", "",
            _now.AddMinutes(-31), null, null, "Instructions: hotfix for the checkout timeout")
        {
            Org = "contoso",
            Project = "platform",
            ApprovalId = "8a2d",
            Details = DetailList.Of(("Pipeline", "platform-hotfix"), ("Run", "20261013.5"), ("Project", "platform"),
                ("Created", _now.AddMinutes(-31).ToString("yyyy-MM-dd HH:mm"))),
        };
        var review = Pr(ReviewKey, "fabrikam/customer-platform/checkout-service-api", 482,
            "Add retry with jitter to payment gateway client", "alice", "feature/payment-retries", "9f3c2ab", 47);
        var review2 = Pr(Review2Key, "fabrikam/customer-platform/web-portal", 479,
            "Update dependency versions for the portal build", "bob", "chore/deps", "41d07e9", 190);
        var ci = new InboxItem(CiKey, Provider.GitHub, ItemKind.MyPrCiFailing, "acme/api", "Migrate settings loader to options pattern",
            "https://github.com/acme/api/pull/57", "you", _now.AddHours(-3), "c0ffee1", 120, "CI failing: build-and-test")
        {
            Number = 57,
            Details = DetailList.Of(("Author", "you"), ("Branch", "settings-options → main"), ("Commit", "c0ffee1"),
                ("Created", _now.AddDays(-1).ToString("yyyy-MM-dd HH:mm"))),
        };
        var ready = new InboxItem(ReadyKey, Provider.GitHub, ItemKind.MyPrReadyToMerge, "acme/web", "Add health endpoint",
            "https://github.com/acme/web/pull/31", "you", _now.AddHours(-5), "beef001", 22, "Approved · checks green")
        { Number = 31 };
        var waiting1 = new InboxItem("gh:waiting:fabrikam/api#88", Provider.GitHub, ItemKind.MyPrWaiting, "fabrikam/api", "Cache token lookups",
            "https://github.com/fabrikam/api/pull/88", "you", _now.AddHours(-9), "beef002", 64, "Waiting on review")
        { Number = 88 };
        var waiting2 = new InboxItem("ado:waiting:contoso/platform/portal#214", Provider.Ado, ItemKind.MyPrWaiting, "contoso/platform/portal",
            "Switch portal to the new auth client", "https://dev.azure.com/contoso/platform/_git/portal/pullrequest/214", "you", _now.AddHours(-20),
            "beef003", null, "Checks running")
        { Number = 214 };
        var snoozed = new InboxItem(SnoozedKey, Provider.GitHub, ItemKind.ReviewRequested, "fabrikam/web", "Bump analyzer versions",
            "https://github.com/fabrikam/web/pull/203", "bob", _now.AddHours(-2), "beef004", 12, null)
        { Number = 203 };
        var muted = new InboxItem(MutedKey, Provider.GitHub, ItemKind.ReviewRequested, "acme/legacy-billing", "Fix rounding in invoice totals",
            "https://github.com/acme/legacy-billing/pull/12", "alice", _now.AddHours(-6), "beef005", 18, null)
        { Number = 12 };
        _entries =
        [
            new(approval, "ADO:contoso", false),
            new(approval2, "ADO:contoso", false),
            review,
            review2,
            new(ci, "GitHub", false),
            new(ready, "GitHub", false),
            new(waiting1, "GitHub", false),
            new(waiting2, "ADO:contoso", false),
            new(snoozed, "GitHub", false),
            new(muted, "GitHub", false),
        ];
        _runs = new()
        {
            [CiKey] = new AgentRun
            {
                Key = CiKey,
                Profile = "pr-shepherd",
                State = AgentState.Ready,
                SessionId = "sample",
                WorktreePath = @"C:\sample",
                HeadSha = "c0ffee1",
                Summary = "Fixed: options binding test expects the old section name",
                CostUsd = 0.31m,
                DurationSeconds = 184,
                FinishedAt = _now.AddMinutes(-40),
                ResultText = ShepherdResult,
            },
        };
    }

    InboxEntry Pr(string key, string repo, int number, string title, string author, string branch, string sha, int lines) =>
        new(new InboxItem(key, Provider.Ado, ItemKind.ReviewRequested, repo, title, $"https://dev.azure.com/fabrikam/_git/pr/{number}",
            author, _now.AddHours(-number % 7 - 1), sha, lines, null)
        {
            Number = number,
            Details = DetailList.Of(("Project", "customer-platform"), ("Repo", repo[(repo.LastIndexOf('/') + 1)..]), ("Author", author),
                ("Branch", $"{branch} → main"), ("Commit", sha)),
        }, "ADO:fabrikam", false);

    const string ShepherdResult = """
        ## PR #57 — CI shepherd

        **Status:** CI was red on `build-and-test`; root cause found, fix suggested (not pushed).

        ### Failure
        - `SettingsLoaderTests.Binds_from_section` failed:
          expected section `App:Settings`, found `Settings`.
        - The options registration now binds `Settings` (new name),
          but the test fixture still writes `App:Settings`.

        ### Suggested fix
        1. Update `tests/fixtures/appsettings.test.json` to the new section name.
        2. Keep a fallback read of `App:Settings` for one release (see note).

        ### Other checks
        - lint: passing
        - unit (other projects): passing
        - integration: skipped on PRs

        SUMMARY: one test fixture uses the old section name; update it and CI goes green.
        """;

    // One muted row (acme/legacy-billing) and one snoozed row (fabrikam/web); waiting on others collapsed, snoozed expanded.
    InboxView View() => InboxView.Compose(Empty ? [] : _entries.Where(e => !HiddenKeys.Contains(e.Item.Key)).ToList(), new FiltersConfig { Mute = ["gh:acme/legacy-*"] }, [],
        [new Snooze(SnoozedKey, _now.AddMinutes(130), false)], _now);

    /// <summary>The clock the confirm guard reads; tests advance it to pass the 600 ms guard without sleeping.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.Now;
    /// <summary>"Waiting on others" is expanded (the popup's section toggle).</summary>
    public bool WaitingExpanded { get; set; }
    public bool SnoozedExpanded { get; set; } = true;
    /// <summary>Every command the popup sent, as short strings ("toggle gh:ci:acme/api#57", "confirm … comment=hi"). Tests assert on it.</summary>
    public List<string> Calls { get; } = [];
    /// <summary>Per-item previews returned by <see cref="BeginActionAsync"/> (an allowed preview naming the action when none is set).</summary>
    public Dictionary<string, ActionPreview> Previews { get; } = new();

    public void Raise() => Changed?.Invoke();

    public IReadOnlyList<InboxSection> BuildSections(string? query)
    {
        var searching = !string.IsNullOrWhiteSpace(query);
        var sections = InboxPresenter.Build(View(), k => _runs.GetValueOrDefault(k), Decide, (_, _) => null, Clock(),
            ActionText.DefaultActions, k => States.GetValueOrDefault(k), r => RepoNames.Display(r, null, 34),
            waitingExpanded: WaitingExpanded || searching, snoozedExpanded: SnoozedExpanded || searching);
        sections = InboxFilter.Apply(sections, query);
        return searching ? sections.Select(s => s.BatchButtons ? s with { BatchButtons = false } : s).ToList() : sections;
    }

    /// <summary>Unmuted active rows (the muted, snoozed and waiting rows don't count).</summary>
    public int HeaderCount => View().BadgeCount;

    static AgentDecision Decide(InboxItem i) => i.Kind switch
    {
        ItemKind.ReviewRequested => new AgentDecision(AgentMode.Button, "peer-review", new AgentProfile()),
        ItemKind.MyPrCiFailing => new AgentDecision(AgentMode.Button, "pr-shepherd", new AgentProfile()),
        _ => new AgentDecision(AgentMode.None, null, null),
    };

    public IReadOnlyList<SourceStatus> Sources() =>
    [
        new("GitHub", true, null, _now),
        new("ADO:contoso", true, null, _now),
        new("ADO:fabrikam", true, null, _now),
    ];

    public string? ConfigError { get; set; }
    public DateTimeOffset? LastPolledAt => _now.AddMinutes(-1);
    public bool IsRefreshing => false;
    public event Action? Changed;

    public void OpenUrl(string url) => Calls.Add("open " + url);
    public void RunAgent(string key) => Calls.Add("run " + key);
    public void OpenInClaude(string key) => Calls.Add("claude " + key);

    public void ToggleExpand(string key)
    {
        Calls.Add("toggle " + key);
        var next = RowStateMachine.Toggle(States.GetValueOrDefault(key));
        if (next.Mode == RowMode.Expanded)
            foreach (var (k, s) in States.ToList())
                if (k != key && s.Mode != RowMode.Sending) States.Remove(k);
        if (next.Mode == RowMode.Collapsed) States.Remove(key);
        else States[key] = next;
        Changed?.Invoke();
    }

    public async Task BeginActionAsync(string key, ItemAction action)
    {
        Calls.Add($"begin {key} {action}");
        if (States.GetValueOrDefault(key) is { Mode: RowMode.Sending or RowMode.Done }) return;
        var begun = RowStateMachine.Begin(action, Clock());
        States[key] = begun;
        Changed?.Invoke();
        await Task.Yield();
        var preview = Previews.GetValueOrDefault(key) ?? new ActionPreview(true, $"{ActionText.ButtonLabel(action).TrimEnd('…')} {key}?",
            ActionText.ButtonLabel(action).TrimEnd('…'), null, null, null);
        if (States.TryGetValue(key, out var current) && ReferenceEquals(current, begun))
            States[key] = RowStateMachine.WithPreview(begun, preview, Clock());
        Changed?.Invoke();
    }

    public void CancelAction(string key)
    {
        Calls.Add("cancel " + key);
        if (States.GetValueOrDefault(key) is not { Mode: RowMode.Confirming or RowMode.Failed or RowMode.Done } s) return;
        if (s.Mode == RowMode.Done) States.Remove(key);
        else States[key] = RowStateMachine.Cancel(s);
        Changed?.Invoke();
    }

    public Task ConfirmActionAsync(string key, ActionInput input)
    {
        // Like AppController: the button is not trusted, the guard is re-checked here.
        if (States.GetValueOrDefault(key) is not { } state || !RowStateMachine.CanConfirm(state, Clock())) return Task.CompletedTask;
        Calls.Add($"confirm {key} comment={input.Comment} method={input.Method} delete={input.DeleteBranch}");
        var sending = RowStateMachine.Sending(state);
        States[key] = RowStateMachine.Finished(sending, new ActionResult(true, null));
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public BatchCoordinator Batch { get; }

    public async Task<string?> BeginBatchAsync(ItemAction action)
    {
        Calls.Add("batch " + action);
        var section = BuildSections(null).FirstOrDefault(s => s.Id == BatchApproval.SectionId);
        if (section is null) return null;
        var rows = BatchApproval.Eligible(section);
        if (rows.Count < BatchApproval.MinItems) return null;
        var entries = rows.Select(r => new BatchEntry(_entries.First(e => e.Item.Key == r.Key).Item, null, r.RepoDisplay)).ToList();
        await Batch.OpenAsync(action, entries, CancellationToken.None);
        return null;
    }

    public void CancelBatch()
    {
        Calls.Add("batch-cancel");
        Batch.Close();
    }

    public Task ConfirmBatchAsync(string? comment)
    {
        Calls.Add($"batch-confirm comment={comment}");
        return Batch.ConfirmAsync(comment, CancellationToken.None);
    }

    /// <summary>Sample data: every preview is allowed and every execute succeeds, so batches and rows can be driven end to end.</summary>
    sealed class SampleActions : IItemActions
    {
        public IReadOnlyList<ItemAction> ActionsFor(InboxItem item) => ActionText.DefaultActions(item);
        public Task<ActionPreview> PreviewAsync(InboxItem item, ItemAction action, AgentRun? run, CancellationToken ct) =>
            Task.FromResult(new ActionPreview(true, $"{ActionText.ButtonLabel(action)} {item.Title}", "Go", null, null, null));
        public Task<ActionResult> ExecuteAsync(InboxItem item, ItemAction action, ActionPreview preview, ActionInput input, CancellationToken ct) =>
            Task.FromResult(new ActionResult(true, null));
    }

    public void OnPopupShown()
    {
        Calls.Add("shown");
        foreach (var (key, s) in States.ToList())
            if (s.Mode == RowMode.Confirming) States[key] = RowStateMachine.Rearm(s, Clock());
        Batch.Rearm();
    }

    public void CycleTheme()
    {
        Calls.Add("theme");
        ThemeMode = ThemeResolver.Next(ThemeMode);
        Changed?.Invoke();
    }

    public Task RefreshAsync()
    {
        Calls.Add("refresh");
        return Task.CompletedTask;
    }

    public void OpenConfig() => Calls.Add("open-config");
    public void OpenLogs() => Calls.Add("open-logs");
    public void OpenSettings() => Calls.Add("open-settings");
    public void Quit() => Calls.Add("quit");
    public bool StartWithWindows { get; set; }
    public string Version => "1.2.0";
    public void CopyText(string text) => Calls.Add("copy " + text.Length);
    public void SnoozeItem(string key, SnoozeChoice choice) => Calls.Add($"snooze {key} {choice}");
    public void Unsnooze(string key) => Calls.Add("unsnooze " + key);
    public void Mute(string key, MuteScope scope, MuteDuration duration) => Calls.Add($"mute {key} {scope} {duration}");
    public IReadOnlyList<ActiveMute> ActiveMutes() => NoMutes ? [] :
        [new ActiveMute("gh:acme/legacy-*", "forever", true), new ActiveMute("ado:contoso/platform", "45m left", false)];
    public void Unmute(string pattern) => Calls.Add("unmute " + pattern);

    public void ToggleSection(string id)
    {
        Calls.Add("section " + id);
        if (id == "waiting") WaitingExpanded = !WaitingExpanded;
        else if (id == "snoozed") SnoozedExpanded = !SnoozedExpanded;
        Changed?.Invoke();
    }

    public UpdateStatus Update { get; set; } = new(UpdatePhase.Ready, "1.2.0", "1.2.1", null, null);
    public int RunningAgents { get; set; }
    public RestartDecision RestartToUpdate(bool confirmed)
    {
        Calls.Add("restart " + confirmed);
        return RunningAgents > 0 && !confirmed ? RestartDecision.NeedsConfirm : RestartDecision.Restarting;
    }

    public Task CheckForUpdatesAsync()
    {
        Calls.Add("check-updates");
        return Task.CompletedTask;
    }
}
