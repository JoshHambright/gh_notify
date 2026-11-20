# Pipeline Subscriptions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user subscribe to a pipeline (ADO Pipelines or a GitHub Actions workflow), choose which outcomes and scopes alert, and get a toast plus a "Pipelines" popup row that stays until dismissed.

**Architecture:** One provider-neutral `PipelineSource` in `GhNotify.Core` does baselining, matching, carry-forward and dismissal. It reads normalized runs from an `IPipelineRunFeed` with an ADO implementation and a GitHub implementation. Results are `InboxItem`s of a new `ItemKind.PipelineRun`, so the existing reconciler, planner, popup, rules and hooks apply. A new Settings › Pipelines page edits `pipelineSubscriptions` in `config.json`.

**Tech Stack:** C# / .NET 10, Avalonia UI, xUnit (`tests/GhNotify.Tests`, `tests/GhNotify.UI.Tests`), System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-11-20-pipeline-subscriptions-design.md` (read it first; this plan only records decisions the spec leaves to the implementer).

## Global Constraints

- Config JSON uses camelCase, enums by camelCase name (`Json.Options`); unknown keys must still round-trip (`AppConfig.Extra`).
- State enums are stored by name, so `ItemKind.PipelineRun` is appended at the end of the enum. Old `state.json` and `config.json` files without the new fields must load. Upgrade compatibility only.
- Outcomes: `Failed`, `Succeeded`, `PartiallySucceeded` (hidden for GitHub). Cancelled never alerts.
- Baseline: nothing that **finished** before the subscription's `createdAt` ever alerts. API queries use a window starting 1 day before `createdAt`; filter by finish time.
- Item key: `pipe:{provider}:{scope}:{pipelineId}:{runId}:{attempt}[:{stage}]` (`provider` = `ado`/`gh`; `scope` = `org/project` or `owner/repo`).
- Badge and tray attention count only **Normal failed** pipeline rows. Succeeded / with-issues rows toast and show in the section but never count.
- `onlyMine` fails closed when identity is unknown. One row per run+attempt+stage, owned by the first matching subscription in config order.
- Dismissed keys are stored with the run's finish time and pruned after 14 days; run lookback after baseline is 3 days; tune only with a reason.
- Source names: `ADO-Pipelines:{org}` and `GitHub-Pipelines`. Release notes go in `docs/releases/v<next>.md` (check `version.txt` and `docs/releases/TEMPLATE.md`).
- Coverage floor in `build/coverage-floor.txt` must still pass. Run tests with `dotnet test GhNotify.slnx`.

## Review Focus

1. A pipeline run that **re-runs** (GitHub `run_attempt` 2, ADO stage retry): old failure stays, the new attempt is a new row, and a dismissed failure does not return.
2. A subscription created while old failures exist: **no rows or toasts** for them, and no summary toast on the first poll.
3. A source poll that **fails** (rate limit, 403, timeout): carried-forward rows stay and nothing is dismissed or lost.
4. Removing or disabling one subscription out of several that share a source: **only its rows** disappear; the others stay.
5. A stage that is skipped, never started, or still running never alerts; a run still in progress with a finished PROD stage does alert.

## File Structure

| File | Responsibility |
|---|---|
| `src/GhNotify.Core/Pipelines/PipelineModels.cs` (new) | `PipelineOutcome`, `PipelineRunInfo`, `PipelineStageInfo`, `PipelineHit` |
| `src/GhNotify.Core/Pipelines/PipelineMatcher.cs` (new) | Pure matching of a run against a subscription; item and key creation |
| `src/GhNotify.Core/Pipelines/PipelineSource.cs` (new) | `IInboxSource` that polls an `IPipelineRunFeed`, carries rows forward, honours dismissals |
| `src/GhNotify.Core/Config/AppConfig.cs`, `ConfigValidator.cs` | `PipelineSubscription` config and validation |
| `src/GhNotify.Core/Model/InboxItem.cs` | `ItemKind.PipelineRun`; `SubscriptionId`, `Finished` init properties |
| `src/GhNotify.Ado/AdoPipelineFeed.cs`, `src/GhNotify.GitHub/GitHubPipelineFeed.cs` (new) | Provider feeds and pipeline pickers |
| `src/GhNotify.UI/Settings/Pages/PipelinesPage.cs` (new) | Settings page |

---

### Task 0: Verify the provider APIs (spike, no product code)

**Files:**
- Create: `docs/superpowers/notes/2026-11-20-pipeline-api-verification.md`

**Interfaces:** Produces: the written answers Tasks 5 and 6 rely on.

- [ ] **Step 1:** With `az` (ADO) and `gh` (GitHub), against any real pipeline, record in the note the JSON shape and a working URL for each: ADO `_apis/build/definitions`, `_apis/build/builds?definitions=&minTime=&queryOrder=finishTimeDescending` (note which time `minTime` filters, and that in-progress builds are returned without `statusFilter`), `_apis/build/builds/{id}/timeline` (`Stage` records: `name`, `result`, `state`, `finishTime`, `attempt`), and `requestedFor.id`. GitHub: `/repos/{r}/actions/workflows`, `/actions/workflows/{id}/runs` (fields `run_attempt`, `triggering_actor.login`, `head_branch`, `updated_at`, `conclusion`), `/actions/runs/{id}/jobs`, and how a job maps to an environment (job name vs `/repos/{r}/deployments` + statuses).
- [ ] **Step 2:** Record decisions: how GitHub "stage" is derived (job name, environment, or both) and what UI label to use; whether `az`/`gh` tokens need new scopes (if so, add to `docs/security.md`).
- [ ] **Step 3:** Commit: `git add docs/superpowers/notes && git commit -m "docs: pipeline API verification notes"`

---

### Task 1: Core model, config, state and kind plumbing

**Files:**
- Modify: `src/GhNotify.Core/Model/InboxItem.cs`, `src/GhNotify.Core/Config/AppConfig.cs`, `src/GhNotify.Core/Config/ConfigValidator.cs`, `src/GhNotify.Core/State/StateStore.cs`, `src/GhNotify.Core/Inbox/NotificationPolicy.cs`, `src/GhNotify.Core/Inbox/NotificationPlanner.cs`, `src/GhNotify.Core/Presentation/InboxPresenter.cs`, `src/GhNotify.Core/Settings/SettingsDraft.cs`, `src/GhNotify.UI/Settings/SettingsRows.cs`
- Create: `src/GhNotify.Core/Pipelines/PipelineModels.cs`
- Test: `tests/GhNotify.Tests/Core/PipelineConfigTests.cs`, extend `ConfigValidatorTests.cs`, `StateStoreTests.cs`, `NotificationPlannerTests.cs`, `InboxPresenterTests.cs`

**Interfaces:**
- Produces:
  - `enum ItemKind { …, PipelineRun }` (appended).
  - `InboxItem.SubscriptionId: string?` and `InboxItem.Finished: DateTimeOffset?` (init properties).
  - `enum PipelineOutcome { Failed, Succeeded, PartiallySucceeded }`.
  - `class PipelineSubscription { string Id; Provider Provider; string? Org; string? Project; string? Repo; long PipelineId; string Pipeline; List<PipelineOutcome> On; List<string> Stages; List<string> Branches; bool OnlyMine; bool Enabled = true; DateTimeOffset CreatedAt; }` and `AppConfig.PipelineSubscriptions: List<PipelineSubscription>`.
  - `AppState.DismissedPipelineRuns: List<DismissedRun>` with `record DismissedRun(string Key, DateTimeOffset Finished)`.
  - `PipelineSubscription.Scope` computed: `org/project` for ADO, `Repo` for GitHub.

- [ ] **Step 1: Write failing tests.**
  - `PipelineConfigTests.RoundTripsCamelCase`: JSON `{"pipelineSubscriptions":[{"id":"a","provider":"ado","org":"acme","project":"P","pipelineId":42,"pipeline":"Deploy","on":["failed"],"stages":["PROD*"],"branches":["main"],"onlyMine":true,"enabled":true,"createdAt":"2026-11-20T09:00:00Z"}]}` loads into the typed object and saves back equal; a config with no key loads with an empty list.
  - `ConfigValidatorTests`: errors (field `pipelineSubscriptions[0].…`) for empty `id`, duplicate ids, ADO without org or project, GitHub without `owner/repo`, empty `on`, `PartiallySucceeded` on a GitHub subscription, `pipelineId <= 0`, null entries.
  - `StateStoreTests.OldStateWithoutDismissedLoads`; `DismissedRoundTrips`.
  - `NotificationPlannerTests`: a `PipelineRun` item toasts with title "Pipeline failed" / "Pipeline succeeded" / "Pipeline succeeded with issues" (from `Detail`), and a failed run sorts before a succeeded one when both are candidates, both after `MyPrCiFailing`.
  - `InboxPresenterTests`: `PipelineRun` rows appear in a "Pipelines" section (id `"PipelineRun"`).
- [ ] **Step 2:** Run `dotnet test tests/GhNotify.Tests --filter "FullyQualifiedName~Pipeline|ConfigValidator|StateStore|NotificationPlanner|InboxPresenter"`. Expected: FAIL (types missing).
- [ ] **Step 3:** Implement the interfaces above. `NotificationPolicy.TitleFor` maps `PipelineRun` by `Detail` ("Failed"/"Succeeded"/"PartiallySucceeded" → the three titles). Planner `Priority` appends `PipelineRun`, and orders failures before successes within it via an `OrderBy` on `Detail == "Failed"`. Presenter `Order` gets `(ItemKind.PipelineRun, "Pipelines")`. `SettingsDraft` kind label "Pipeline run"; add the kind to the two lists in `SettingsRows.cs:14,20`. Update `AppController`'s `_usedBefore` is not needed.
- [ ] **Step 4:** Re-run the filter. Expected: PASS; then `dotnet build GhNotify.slnx` to catch any exhaustive switch over `ItemKind`.
- [ ] **Step 5:** Commit: `feat: pipeline subscription model, config and kind plumbing`.

---

### Task 2: Matcher and item factory

**Files:**
- Create: `src/GhNotify.Core/Pipelines/PipelineMatcher.cs`; extend `PipelineModels.cs`
- Test: `tests/GhNotify.Tests/Core/PipelineMatcherTests.cs`

**Interfaces:**
- Consumes: `PipelineSubscription`, `PipelineOutcome`, `FilterPattern`/`ItemMatcher.Glob`.
- Produces:
  - `record PipelineStageInfo(string Name, PipelineOutcome? Outcome, DateTimeOffset? Finished)` (`Outcome` null = not final, skipped or cancelled).
  - `record PipelineRunInfo(Provider Provider, string Scope, long PipelineId, string Pipeline, string RunId, string RunNumber, int Attempt, string? Branch, string? RequestedBy, bool? RequestedByMe, string Url, string? CommitSha, DateTimeOffset? Finished, PipelineOutcome? Outcome, IReadOnlyList<PipelineStageInfo> Stages)`.
  - `record PipelineHit(string? Stage, PipelineOutcome Outcome, DateTimeOffset Finished)`.
  - `static IReadOnlyList<PipelineHit> PipelineMatcher.Match(PipelineSubscription sub, PipelineRunInfo run)`.
  - `static string PipelineMatcher.Key(PipelineRunInfo run, string? stage)` and `static InboxItem PipelineMatcher.ToItem(PipelineSubscription sub, PipelineRunInfo run, PipelineHit hit)` (sets `Kind=PipelineRun`, `Repo=Scope`, `Title="{Pipeline}" or "{Pipeline} · {Stage}"`, `Detail=hit.Outcome.ToString()`, `Url`, `Author=RequestedBy??""`, `UpdatedAt=hit.Finished`, `Finished`, `SubscriptionId`, `HeadSha`, `Details` = Pipeline, Run, Attempt, Stage, Branch, Triggered by, Result, Finished).

- [ ] **Step 1: Write failing tests** (table-style, `[Theory]` where useful):
  - No stages: overall `Failed` matches `on:[Failed]`, not `on:[Succeeded]`; a run with `Outcome` null (in progress) yields no hits.
  - Stages `["PROD*"]` with stages `QA=Succeeded`, `PROD=Failed` and an in-progress run → one hit `Stage="PROD", Failed`; `PROD` with `Outcome=null` → none; a `Prod-EU` and `Prod-US` both failed → two hits.
  - Baseline: a hit whose `Finished < sub.CreatedAt` is dropped; equal or later is kept.
  - `Branches:["main","release/*"]` matches `release/1.2`, rejects `feature/x`; null branch with branches set rejects.
  - `OnlyMine:true` rejects `RequestedByMe == false` and `null`; accepts `true`.
  - `Key` includes attempt and stage: `pipe:ado:acme/P:42:1001:1:PROD`; two attempts give different keys.
  - `ToItem` fields as listed; `Detail` is `"Failed"`.
- [ ] **Step 2:** Run `dotnet test tests/GhNotify.Tests --filter PipelineMatcher`. Expected: FAIL.
- [ ] **Step 3:** Implement. Globs via `ItemMatcher.Glob` (case-insensitive). Disabled subscriptions are the caller's concern, not the matcher's.
- [ ] **Step 4:** Re-run. Expected: PASS.
- [ ] **Step 5:** Commit: `feat: pipeline run matcher and item factory`.

---

### Task 3: PipelineSource (baseline, carry-forward, dismissal, failure handling)

**Files:**
- Create: `src/GhNotify.Core/Pipelines/PipelineSource.cs`
- Modify: `src/GhNotify.Core/Sources/SourceRetirement.cs`
- Test: `tests/GhNotify.Tests/Core/PipelineSourceTests.cs`, extend `SourceRetirementTests.cs`

**Interfaces:**
- Consumes: Task 1 and 2 types.
- Produces:
  - `interface IPipelineRunFeed { Task<IReadOnlyList<PipelineRunInfo>> RecentRunsAsync(PipelineSubscription sub, DateTimeOffset since, bool includeInProgress, CancellationToken ct); }` (throws the usual `SourceAuthException`/`RateLimitedException`/`HttpRequestException`).
  - `sealed class PipelineSource : IInboxSource` with ctor `(string name, IPipelineRunFeed feed, Func<IReadOnlyList<PipelineSubscription>> subscriptions, Func<IReadOnlySet<string>> dismissedKeys, Func<IReadOnlyList<InboxItem>> lastKnown, TimeProvider? time = null)`.
  - `Name` returns `name`; `SourceRetirement.IsDisabled` handles `GitHub-Pipelines` (no enabled GitHub subscription or `!c.GitHub.Enabled`) and `ADO-Pipelines:{org}` (no enabled ADO subscription for that org or `!c.Ado.Enabled`).
  - `static IReadOnlyList<DismissedRun> PipelineSource.PruneDismissed(IReadOnlyList<DismissedRun> d, DateTimeOffset now)` (drops entries older than 14 days).

- [ ] **Step 1: Write failing tests** with a fake feed and a fake clock:
  - New run in window that matches → returned as an item; run with `Finished < CreatedAt` → not returned (no flood).
  - `includeInProgress` is passed `true` only for subscriptions with `Stages` set.
  - Carry-forward: second poll where the feed returns nothing still returns the first poll's item (via `lastKnown`); an item whose key is in `dismissedKeys` is not returned (so the reconciler sees it as removed).
  - Failure: feed throws `HttpRequestException` for sub A → `SourceResult.Ok` still contains A's carried-forward items and sub B's fresh items (one bad subscription never fails the source); `SourceAuthException` / `RateLimitedException` → `SourceResult.Fail`.
  - Carried-forward items whose `SubscriptionId` is not among enabled subscriptions are dropped (removing one subscription removes only its rows).
  - Two subscriptions on the same pipeline matching the same run → one item, owned by the first in list order.
  - Feed called once per subscription per poll; a run id already represented by a carried-forward item (same key) is not re-detailed (feed is asked only for runs, caching of timelines is the feed's job).
  - `SourceRetirementTests`: names retire when their last subscription is removed or the provider is disabled; not otherwise.
  - `PruneDismissed` drops 15-day-old entries, keeps 13-day-old.
- [ ] **Step 2:** Run `dotnet test tests/GhNotify.Tests --filter "PipelineSource|SourceRetirement"`. Expected: FAIL.
- [ ] **Step 3:** Implement. `since = max(sub.CreatedAt - 1 day, now - 3 days)`. Fresh matches ∪ carried-forward pipeline items (filtered as above) → de-duplicate by key (fresh wins) → drop dismissed. Catch per-subscription `HttpRequestException`/`JsonException` only; let auth and rate-limit propagate to `SourceResult.Fail`.
- [ ] **Step 4:** Re-run. Expected: PASS.
- [ ] **Step 5:** Commit: `feat: pipeline source with baseline, carry-forward and dismissal`.

---

### Task 4: Dismiss, badge rule and first-poll behaviour

**Files:**
- Modify: `src/GhNotify.Core/Inbox/InboxView.cs`, `src/GhNotify.Core/Inbox/NotificationPlanner.cs`, `src/GhNotify.Core/Presentation/InboxPresenter.cs`, `src/GhNotify.Core/Presentation/RowMenus.cs`, `src/GhNotify.Core/Presentation/KeyMap.cs`, `src/GhNotify.Core/Actions/ItemActions.cs`, `src/GhNotify.UI/AppController.cs`, `src/GhNotify.UI/PopupWindow.axaml.cs` (row button and section header)
- Test: `tests/GhNotify.Tests/Core/InboxViewTests.cs`, `NotificationPlannerTests.cs`, `InboxPresenterTests.cs`, `RowMenusTests.cs`, `KeyMapTests.cs`; `tests/GhNotify.UI.Tests/AppControllerPipelineTests.cs`

**Interfaces:**
- Consumes: Tasks 1-3.
- Produces: `AppController.DismissPipeline(string key)`, `AppController.DismissAllPipelines()`; `ItemAction.Dismiss` with button label "Dismiss"; `InboxView.BadgeCount` excludes `PipelineRun` items whose `Detail != "Failed"`.

- [ ] **Step 1: Write failing tests.**
  - `InboxViewTests`: three Normal pipeline rows (failed, succeeded, partial) plus one PR row → `BadgeCount == 2`; a muted failed pipeline row doesn't count.
  - `NotificationPlannerTests`: `diff.InitialLoad` containing only pipeline items produces **no summary toast**; pipeline items in `diff.Added` still toast individually.
  - `InboxPresenterTests`: pipeline rows carry a Dismiss action; the Pipelines section exposes a "Dismiss all" header action.
  - `KeyMapTests`/`RowMenusTests`: Dismiss has a key and a menu entry only for pipeline rows.
  - UI `AppControllerPipelineTests` (headless, fake feed): `DismissPipeline(key)` writes `DismissedRun(key, Finished)` to state, removes the row and its toast on the next poll; `DismissAllPipelines()` clears every non-hidden pipeline row; a new run (new key) afterwards appears again.
- [ ] **Step 2:** Run the filtered tests in both projects. Expected: FAIL.
- [ ] **Step 3:** Implement. `NotificationPlanner` filters pipeline items out of the `InitialLoad` summary. `AppController` owns `_dismissed` under `_filterGate` like `_snoozes`, saves it in `AppState.DismissedPipelineRuns`, runs `PipelineSource.PruneDismissed` on poll, and exposes `() => _dismissed keys` to the source. Dismiss triggers an immediate re-poll or local removal so the row disappears at once (follow how `SnoozeItem` calls `Save()` and `Changed`).
- [ ] **Step 4:** Re-run. Expected: PASS.
- [ ] **Step 5:** Commit: `feat: dismiss pipeline rows; only failures light the badge`.

---

### Task 5: ADO pipeline feed

**Files:**
- Create: `src/GhNotify.Ado/AdoPipelineFeed.cs`
- Test: `tests/GhNotify.Tests/Ado/AdoPipelineFeedTests.cs` (canned JSON in `tests/GhNotify.Tests/Fixtures`)

**Interfaces:**
- Consumes: `AdoClient`, `AdoDirectory.GetOrgContextAsync` (`MyIdentityIds`), `IPipelineRunFeed`.
- Produces:
  - `sealed class AdoPipelineFeed(AdoClient client, AdoDirectory directory) : IPipelineRunFeed`.
  - `Task<IReadOnlyList<PipelinePick>> ListPipelinesAsync(string org, string project, CancellationToken ct)` where `record PipelinePick(long Id, string Name)`.
  - `Task<IReadOnlyList<string>> ListProjectsAsync(string org, CancellationToken ct)` via `AdoDirectory.GetProjectsAsync`.

- [ ] **Step 1: Write failing tests** against canned JSON from Task 0: builds list → `PipelineRunInfo` (Outcome map `failed→Failed`, `succeeded→Succeeded`, `partiallySucceeded→PartiallySucceeded`, `canceled→null`; `Attempt` from the build, `Url` from `_links.web.href`, `Branch` with `refs/heads/` stripped, `RequestedByMe` from `requestedFor.id ∈ MyIdentityIds`); timeline `Stage` records → `PipelineStageInfo` (only when `Stages` is set, only for builds new to this feed; finished stage results map likewise, `skipped`/pending → `Outcome null`); in-progress builds included only when `includeInProgress`; 403/404 → `HttpRequestException` propagates; timeline fetched once per build id across polls (in-memory cache keyed by build id + finished-stage count).
- [ ] **Step 2:** Run `dotnet test tests/GhNotify.Tests --filter AdoPipelineFeed`. Expected: FAIL.
- [ ] **Step 3:** Implement per the Task 0 note (`definitions=`, `minTime`, `queryOrder=finishTimeDescending`, `$top`).
- [ ] **Step 4:** Re-run. Expected: PASS.
- [ ] **Step 5:** Commit: `feat: ADO pipeline run feed`.

---

### Task 6: GitHub pipeline feed (after Task 0's stage decision)

**Files:**
- Create: `src/GhNotify.GitHub/GitHubPipelineFeed.cs`
- Test: `tests/GhNotify.Tests/GitHub/GitHubPipelineFeedTests.cs`

**Interfaces:**
- Consumes: `GitHubClient.GetAsync`, `IPipelineRunFeed`; the viewer login the GitHub source already resolves (add a small cached `ViewerLoginAsync` on the feed if none is exposed).
- Produces: `sealed class GitHubPipelineFeed(GitHubClient client) : IPipelineRunFeed`; `Task<IReadOnlyList<PipelinePick>> ListWorkflowsAsync(string repo, CancellationToken ct)`.

- [ ] **Step 1: Write failing tests** with canned JSON: runs → `PipelineRunInfo` (`conclusion success→Succeeded`, `failure|timed_out→Failed`, `cancelled|skipped→null`, `PartiallySucceeded` never produced; `Attempt = run_attempt`; `RequestedByMe = triggering_actor.login == viewer`, `null` when the viewer is unknown; `Branch = head_branch`); stages from jobs per the Task 0 decision (job name and/or environment); jobs fetched only for runs that pass cheap checks and only when `Stages` is set.
- [ ] **Step 2:** Run `dotnet test tests/GhNotify.Tests --filter GitHubPipelineFeed`. Expected: FAIL.
- [ ] **Step 3:** Implement per the Task 0 note (`status` filter dropped when `includeInProgress`; `created=>=since`).
- [ ] **Step 4:** Re-run. Expected: PASS.
- [ ] **Step 5:** Commit: `feat: GitHub pipeline run feed`.

---

### Task 7: Wire sources into the app

**Files:**
- Modify: `src/GhNotify.UI/AppController.cs:100-120`, `src/GhNotify.UI/Settings/ISettingsHost.cs`, `src/GhNotify.UI/Settings/SampleSettingsHost.cs`, `src/GhNotify.UI/SampleHost.cs`
- Create: `src/GhNotify.Core/Pipelines/PipelineSourceCatalog.cs`
- Test: `tests/GhNotify.Tests/Core/PipelineSourceCatalogTests.cs`, `tests/GhNotify.UI.Tests/AppControllerPipelineTests.cs` (extend)

**Interfaces:**
- Produces: `PipelineSourceCatalog(Func<AppConfig> config, Func<string, IInboxSource> adoOrgSource, Func<IInboxSource> gitHubSource) : ISourceCatalog` returning one `ADO-Pipelines:{org}` per org with an enabled ADO subscription (ADO enabled) and `GitHub-Pipelines` when an enabled GitHub subscription exists (GitHub enabled), nothing otherwise. `ISettingsHost` gains `Task<IReadOnlyList<string>> ListAdoProjectsAsync(string org)`, `Task<IReadOnlyList<PipelinePick>> ListAdoPipelinesAsync(string org, string project)`, `Task<IReadOnlyList<PipelinePick>> ListGitHubWorkflowsAsync(string repo)`, `IReadOnlyList<string> KnownGitHubRepos()`.

- [ ] **Step 1: Write failing tests:** catalog output for 0/1/many subscriptions and disabled providers; end-to-end headless test: a fake feed with a post-baseline failing run makes a row and one toast, and a restart (new controller over the saved state) shows the row without re-toasting.
- [ ] **Step 2:** Run the filtered tests. Expected: FAIL.
- [ ] **Step 3:** Implement; add the catalog to the `CompositeCatalog` in `AppController`, passing `() => _reconciler.ItemsFor(name)` and `() => _dismissed` into each `PipelineSource`.
- [ ] **Step 4:** Re-run both test projects. Expected: PASS.
- [ ] **Step 5:** Commit: `feat: wire pipeline sources into the poller`.

---

### Task 8: Settings › Pipelines page

**Files:**
- Create: `src/GhNotify.UI/Settings/Pages/PipelinesPage.cs`
- Modify: `src/GhNotify.Core/Settings/SettingsDraft.cs` (`SettingsPage.Pipelines`, between Workflows and Filters; draft add/update/remove/toggle of subscriptions), `src/GhNotify.UI/Settings/SettingsWindow.axaml(.cs)` (nav item, dot, `_pages`), `src/GhNotify.UI/Settings/SampleSettingsHost.cs`
- Test: `tests/GhNotify.Tests/Core/SettingsDraftTests.cs` (extend), `tests/GhNotify.UI.Tests/PipelinesPageTests.cs`

**Interfaces:**
- Consumes: the Task 7 host methods and the Task 1 config model.
- Produces: `SettingsDraft.AddPipelineSubscription(PipelineSubscription)`, `RemovePipelineSubscription(string id)`, `PipelineSubscriptionSummary(PipelineSubscription) : string` (e.g. `Deploy · acme/Platform · Failed on PROD* · main · mine`).

- [ ] **Step 1: Write failing tests:** summary strings; add generates an id and `CreatedAt = now` (clock injected); remove; `PartiallySucceeded` option absent for GitHub; page lists subscriptions with an enable toggle; add flow (provider → org/project or repo → searchable pipeline list → outcome checkboxes → stage/branch/"only my runs") produces the right draft entry; validation errors from `ConfigValidator` show on the page; Save is blocked while invalid. GitHub stage field labelled per Task 0's decision.
- [ ] **Step 2:** Run filtered tests. Expected: FAIL.
- [ ] **Step 3:** Implement following `WorkflowsPage`/`FiltersPage` row and pill patterns (`PageParts.cs`, `SettingsRows.cs`). Picker loading shows an inline error row on failure, never a modal.
- [ ] **Step 4:** Re-run. Expected: PASS. Run `build/screenshots.ps1` if it covers settings pages, and add `docs/images/settings-pipelines.png` if so.
- [ ] **Step 5:** Commit: `feat: Settings Pipelines page`.

---

### Task 9: Docs, release notes and final checks

**Files:**
- Modify: `docs/notifications.md`, `docs/configuration.md`, `docs/security.md` (only if Task 0 found new scopes), `docs/roadmap.md`, `README.md`
- Create: `docs/releases/v<next>.md` (from `TEMPLATE.md`)

- [ ] **Step 1:** Document: the Pipelines page, each subscription field with the JSON example from the spec, the outcome/stage/branch/onlyMine semantics, baselining, dismiss vs mute/snooze, that only failures light the badge, and the GitHub stage caveat.
- [ ] **Step 2:** Run `dotnet test GhNotify.slnx` and `pwsh build/check-coverage.ps1`; run `pwsh build/leak-scan.ps1`. Expected: all pass.
- [ ] **Step 3:** Commit: `docs: pipeline subscriptions`.
