using GhNotify.Core;

namespace GhNotify.App;

/// <summary>
/// Fixed, neutral sample data (contoso / fabrikam / acme, alice / bob) for --render-preview. Every command is a no-op:
/// it never touches the network, tokens or the shell.
/// </summary>
public sealed class SampleHost : IPopupHost
{
    public const string ApprovalKey = "ado:approval:contoso/platform/7f1c";
    public const string ReviewKey = "ado:review:fabrikam/customer-platform/checkout-service-api#482";
    public const string Review2Key = "ado:review:fabrikam/customer-platform/web-portal#479";
    public const string CiKey = "gh:ci:acme/api#57";

    readonly DateTimeOffset _now = DateTimeOffset.Now;
    readonly List<InboxEntry> _entries;
    readonly Dictionary<string, AgentRun> _runs;

    public Dictionary<string, RowUiState> States { get; } = new();
    public bool Empty { get; set; }
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;

    public SampleHost()
    {
        var approval = new InboxItem(ApprovalKey, Provider.Ado, ItemKind.ApprovalPending, "contoso/platform",
            "platform-release 20261013.4", "https://dev.azure.com/contoso/platform/_build/results?buildId=1042", "",
            _now.AddMinutes(-12), null, null, "Instructions: check the release notes before approving")
        {
            Org = "contoso", Project = "platform", ApprovalId = "7f1c",
            Details = DetailList.Of(("Pipeline", "platform-release"), ("Run", "20261013.4"), ("Project", "platform"),
                ("Environment", "production"), ("Created", _now.AddMinutes(-12).ToString("yyyy-MM-dd HH:mm"))),
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
        _entries =
        [
            new(approval, "ADO:contoso", false),
            review,
            review2,
            new(ci, "GitHub", false),
        ];
        _runs = new()
        {
            [CiKey] = new AgentRun
            {
                Key = CiKey, Profile = "pr-shepherd", State = AgentState.Ready, SessionId = "sample", WorktreePath = @"C:\sample",
                HeadSha = "c0ffee1", Summary = "Fixed: options binding test expects the old section name", CostUsd = 0.31m,
                DurationSeconds = 184, FinishedAt = _now.AddMinutes(-40), ResultText = ShepherdResult,
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

    public IReadOnlyList<InboxSection> BuildSections() =>
        InboxPresenter.Build(Empty ? [] : _entries, k => _runs.GetValueOrDefault(k), Decide, (_, _) => null, _now,
            ActionText.DefaultActions, k => States.GetValueOrDefault(k), r => RepoNames.Display(r, null, 34));

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

    public string? ConfigError => null;
    public DateTimeOffset? LastPolledAt => _now.AddMinutes(-1);
    public bool IsRefreshing => false;
    public event Action? Changed { add { } remove { } }

    public void OpenUrl(string url) { }
    public void RunAgent(string key) { }
    public void OpenInClaude(string key) { }
    public void ToggleExpand(string key) { }
    public Task BeginActionAsync(string key, ItemAction action) => Task.CompletedTask;
    public void CancelAction(string key) { }
    public Task ConfirmActionAsync(string key, string? comment) => Task.CompletedTask;
    public void OnPopupShown() { }
    public void CycleTheme() { }
    public Task RefreshAsync() => Task.CompletedTask;
    public void OpenConfig() { }
    public void OpenLogs() { }
    public void Quit() { }
    public void CopyText(string text) { }
}
