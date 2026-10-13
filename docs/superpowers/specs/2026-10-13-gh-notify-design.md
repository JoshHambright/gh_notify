# gh_notify — "Waiting on me" inbox for GitHub + Azure DevOps

Date: 2026-10-13
Status: Approved 2026-10-13

## 1. Purpose

A tiny Windows tray app that shows a live inbox of everything on GitHub and
Azure DevOps that is waiting on me, toasts me when something new arrives during
work hours, and clears items (and their toasts) on its own once they are no
longer waiting. Optionally it pre-runs Claude agents (`/peer-review`,
`/pr-shepherd`) so the work is already analysed by the time I open it. Optionally a user can wire it into custom commands and hooks based on their local skills and workflows

Gitify was tried and falls short because it replays the notifications feed:
items persist after the PR is merged or approved, and team review requests or
read notifications get lost. This app instead **reconciles against live state**
on every poll.

### Success criteria

- I can glance at the tray badge and know how many things are waiting on me.
- A new review request / pipeline approval / red PR toasts within ~2 minutes,
  during work hours only.
- When an item is resolved (merged, approved, voted, closed, run no longer
  waiting), it disappears from the inbox and its toast is removed from Action
  Center within one poll.
- For repos I opt in, a `/peer-review` findings report is ready before I look,
  and I can resume that exact Claude session with one click.
- No secrets to manage: auth comes from existing `gh` and `az` logins.
- Nothing is ever posted to a PR or pushed by an unattended agent.

### Decisions already made

| Topic | Decision |
|---|---|
| Platforms | GitHub and ADO in v1, GitHub built first, behind a provider interface |
| Form factor | .NET Windows tray app with a popup inbox and native toasts |
| Inbox items | Review requests, pipeline approvals, my PRs with CI failing, my PRs needing a response |
| Agent trigger | Per-repo rules auto-run; everything else gets a one-click button |
| Working copy | Discover existing clones under configured roots; each agent runs in a throwaway git worktree |
| Unattended shepherd | Diagnose + notify only; no commits, pushes, or thread replies |
| Hand-off | "Open in Claude" launches a terminal running `claude --resume <session>` in the worktree |
| Toasts | Configurable work hours (default Mon–Fri 08:00–18:00), ~2 min poll |

## 2. Architecture

```
 ┌───────────── GhNotify.App (WPF, tray) ─────────────┐
 │ TrayIcon+badge   InboxPopup   ToastPresenter        │
 └───────▲───────────────▲──────────────▲─────────────┘
         │ InboxChanged  │ agent state  │ Added/Removed
 ┌───────┴───────────────┴──────────────┴─────────────┐
 │ GhNotify.Core                                       │
 │  Poller ─► IInboxSource[] ─► Reconciler ─► Store    │
 │  RulesEngine   Config   WorkHours                   │
 └───────▲───────────────────────────▲────────────────┘
         │                           │
 ┌───────┴────────┐ ┌──────────────┐ ┌─┴────────────────┐
 │ GhNotify.GitHub│ │GhNotify.Ado  │ │ GhNotify.Agents  │
 │ (gh token)     │ │(az token)    │ │ RepoLocator      │
 └────────────────┘ └──────────────┘ │ WorktreeManager  │
                                     │ ClaudeRunner     │
                                     └──────────────────┘
```

Solution layout (`net10.0-windows10.0.19041.0` for App; `net10.0` elsewhere):

| Project | Responsibility | Depends on |
|---|---|---|
| `GhNotify.Core` | Models, poller, reconciler, rules, config, state store, work hours | — |
| `GhNotify.GitHub` | `IInboxSource` for GitHub (GraphQL + REST) | Core |
| `GhNotify.Ado` | `IInboxSource` for Azure DevOps (REST) | Core |
| `GhNotify.Agents` | Repo discovery, worktrees, launching/resuming `claude` | Core |
| `GhNotify.App` | Tray icon, popup, toasts, composition root | all |
| `GhNotify.Tests` | xUnit tests for Core, providers (fixtures), Agents (fake process) | all but App |

Packages: `H.NotifyIcon.Wpf` (tray), `Microsoft.Toolkit.Uwp.Notifications`
(toasts + Action Center history removal for unpackaged apps). No other
runtime dependencies.

## 3. Core model

```csharp
enum Provider { GitHub, Ado }
enum ItemKind { ReviewRequested, ApprovalPending, MyPrCiFailing, MyPrNeedsResponse }

record InboxItem(
    string Key,          // stable identity, e.g. "gh:review:org/repo#123", "ado:approval:org/proj/<approvalId>"
    Provider Provider,
    ItemKind Kind,
    string Repo,         // "org/repo" or "org/project/repo"
    string Title,
    string Url,          // browser link
    string Author,
    DateTimeOffset UpdatedAt,
    string? HeadSha,     // PR head commit; null for approvals
    int? ChangedLines,   // for rules; null if unknown
    string? Detail);     // e.g. "Environment: prod", "2 checks failing", "3 unresolved threads"

interface IInboxSource {
    string Name { get; }                       // "GitHub", "ADO:contoso"
    Task<SourceResult> FetchAsync(CancellationToken ct);
}

record SourceResult(bool Healthy, IReadOnlyList<InboxItem> Items, string? Problem);
// Problem examples: "gh not logged in — run `gh auth login`", "az token expired — run `az login`"
```

One PR can produce more than one item (e.g. my PR that is both CI-failing and
needs-response). Each has its own Key, so they come and go independently.

## 4. Polling and reconciliation

- `Poller` runs every `pollSeconds` (default 120) and on "Refresh now". All
  sources are fetched in parallel; each source has its own timeout (30 s).
- `Reconciler` diffs the previous snapshot against the new one, per source:
  - **Added** keys → `InboxChanged` + toast (if allowed, see below).
  - **Removed** keys → `InboxChanged` + remove the toast from Action Center by tag.
  - **Changed** (same key, different `HeadSha`/`Detail`/`Title`) → update the row
    silently; if `HeadSha` changed, mark any finished agent result **Outdated**.
- **An unhealthy source never causes removals.** Its last good items are kept
  and shown as stale, and the popup footer shows the source's `Problem`.
  This prevents "everything cleared" when a token expires.
- **First poll after start** fills the inbox without one toast per item; it
  sends a single summary toast ("7 items waiting on you") if any exist.
- Toasts are allowed only inside work hours (config: days + start/end, local
  time). Outside work hours the inbox and badge still update.
- Toast tag = first 16 hex chars of SHA-256(Key) (tags are length-limited).
  Clicking a toast opens the item's URL.

State persisted to `%LOCALAPPDATA%\GhNotify\state.json`: last snapshot per
source (so a restart doesn't re-toast everything) and agent runs (§7).

## 5. GitHub source

Auth: `gh auth token` (cached 30 min; re-read on 401). Host: github.com in v1.
Rate budget: ~3–6 GraphQL calls per poll, well under 5000 points/hour.

| Kind | Query | Leaves the inbox when |
|---|---|---|
| ReviewRequested | GraphQL search `is:pr is:open archived:false review-requested:@me` (includes team requests); drafts excluded unless `includeDrafts` | I submit a review (GitHub removes the request), or PR closed/merged |
| MyPrCiFailing | GraphQL search `is:pr is:open author:@me archived:false`, read `commits(last:1).statusCheckRollup.state` ∈ {FAILURE, ERROR} | checks go green/pending, or PR closed/merged |
| MyPrNeedsResponse | same query: `reviewDecision == CHANGES_REQUESTED`, or ≥1 unresolved review thread whose last comment is not mine | I reply/resolve/push and the condition clears, or PR closed/merged |
| ApprovalPending | see below | run no longer `waiting`, or no pending environment where `current_user_can_approve` |

**Deployment approvals** have no "everything waiting for me" API, so it's two steps:

1. *Candidate repos* = union of:
   - repos from notifications (`GET /notifications?all=true&since=<7d>`) whose
     reason is `approval_requested`;
   - `github.watchRepos` from config (explicit list, for repos where
     notifications are unreliable).
2. For each candidate repo: `GET /repos/{r}/actions/runs?status=waiting`, then
   `GET .../runs/{id}/pending_deployments`; one item per run that has an
   environment with `current_user_can_approve == true`. Detail = environment names.

> **Verify first (plan task 1):** that GitHub sends an `approval_requested`
> notification for deployment reviews to team-based required reviewers. If it
> doesn't, discovery falls back to `watchRepos` plus repos where I have open
> PRs or review requests.

## 6. Azure DevOps source

Auth: `az account get-access-token --resource 499b84ac-1321-427f-aa17-267ca6b56798`
(the ADO resource ID). One Entra token covers every org I can access. It is
cached until `expiresOn - 5 min`. If az is not logged in, the source is
unhealthy with "run `az login`".

Identity: `GET https://app.vssps.visualstudio.com/_apis/profile/profiles/me` →
my id. The groups I belong to (needed for team reviewers and group approvers)
are resolved through the Identities API (`queryMembership=expanded`) and
cached for 24 h.

Orgs: by default (`ado.discoverOrgs: true`) the list comes
from `_apis/accounts?memberId=<me>`, minus `ado.excludeOrgs`, plus any `ado.orgs`. Each org is its own `IInboxSource`
("ADO:contoso"), so one broken org doesn't hide the others.

| Kind | Call (per org) | Waiting-on-me test | Leaves the inbox when |
|---|---|---|---|
| ReviewRequested | `_apis/git/pullrequests?searchCriteria.status=active&searchCriteria.reviewerId=<id>` for me and each of my groups | my reviewer entry (or my group's) has `vote == 0` and isn't declined; PR not draft | I vote, or the PR completes or is abandoned |
| MyPrCiFailing | `...pullrequests?searchCriteria.creatorId=<me>&status=active`, then `_apis/policy/evaluations?artifactId=vstfs:///CodeReview/CodeReviewId/{projectId}/{prId}` | any build-type policy evaluation `rejected` | the evaluation is no longer rejected, or the PR closes |
| MyPrNeedsResponse | same PRs + `.../threads` | any reviewer vote −10 (rejected) or −5 (waiting for author), or an active thread whose last comment is by someone else (system threads ignored) | the condition clears |
| ApprovalPending | `_apis/projects`, then per project `_apis/pipelines/approvals?state=pending&$expand=steps` | a pending step whose `assignedApprover` is me or one of my groups | the approval is approved, rejected, or times out |

Project lists are cached for 1 h. Per-project approval calls run with a
concurrency limit of 4.

## 7. Claude agents

### Rules

`rules` in config are evaluated top to bottom and the first match wins. With no
match, the default is `button`.

```jsonc
"rules": [
  { "match": { "author": "dependabot[bot]" },               "agent": "none" },
  { "match": { "kind": "ReviewRequested", "maxChangedLines": 20 }, "agent": "button" },
  { "match": { "repo": "my-org/core-*", "kind": "ReviewRequested" }, "agent": "auto" },
  { "match": { "kind": "MyPrCiFailing" },                    "agent": "auto" }
]
```

Match fields (all optional, ANDed): `provider`, `repo` (glob), `kind`, `author`,
`maxChangedLines`. `agent`: `auto` | `button` | `none`. `profile` (optional) is
the name of an agent profile to run. If it's omitted, the kind's default
profile is used: ReviewRequested → `peer-review`; MyPrCiFailing and
MyPrNeedsResponse → `pr-shepherd`; ApprovalPending → none, so a rule must name
a profile for an approval to get an agent.

### Agent profiles (custom commands)

A profile says which local skill or command to run and how. `peer-review` and
`pr-shepherd` ship as built-in profiles. Users can override them or add their
own in config to wire in any local skill or workflow:

```jsonc
"agentProfiles": {
  "peer-review":  { "prompt": "/peer-review {url} — run unattended: complete the review through writing the findings report, then STOP. Do not post anything. End with a 1–3 line summary starting \"SUMMARY:\"." },
  "pr-shepherd":  { "prompt": "/pr-shepherd {url} — diagnose only: find the real reason for each failure and triage comments. Make no commits, pushes, or thread replies. End with a 1–3 line summary starting \"SUMMARY:\"." },
  "deploy-check": { "prompt": "/post-deploy-verify {url} — report only. End with \"SUMMARY:\".", "needsWorktree": false,
                    "allowedTools": ["Bash(az:*)"] }
}
```

Profile fields:

- `prompt`: required. Placeholders `{url} {repo} {number} {title} {kind}
  {provider} {author} {detail}` are filled in before the run.
- `needsWorktree`: default true. If false, the profile runs in
  `%LOCALAPPDATA%\GhNotify\scratch\<tag>` with no clone required, which is
  useful for approvals.
- `allowedTools` / `disallowedTools`: appended to the global lists.
  The global deny list always applies.

### Event hooks

For non-Claude automation (a script, a Teams webhook, logging), `hooks` runs a
process when something happens:

```jsonc
"hooks": [
  { "event": "ItemAdded",     "match": { "kind": "ApprovalPending" }, "run": ["pwsh", "-File", "C:\\scripts\\ping-teams.ps1"] },
  { "event": "AgentFinished", "run": ["pwsh", "-File", "C:\\scripts\\log-agent.ps1"] }
]
```

- Events: `ItemAdded`, `ItemRemoved`, `ItemChanged`, `AgentFinished`.
- `match` uses the same fields as rules.
- `run` is an argument array, started directly with no shell, so there are
  no quoting problems.
- The process gets the item (plus the agent result for `AgentFinished`) as
  JSON on stdin and as `GHN_*` environment variables (`GHN_EVENT`, `GHN_KEY`,
  `GHN_URL`, `GHN_KIND`, `GHN_REPO`).
- Hooks are fire-and-forget with a 60 s timeout. Failures are logged and
  never block polling.
- Hooks don't fire for items loaded silently on the first poll.

### Running an agent

1. **RepoLocator** scans `repoRoots` (default `C:\dev`, depth 3) for git
   repos and maps each one's `origin` URL (normalised: GitHub https/ssh, ADO
   `dev.azure.com` / `ssh.dev.azure.com` / `visualstudio.com` forms) to its
   path. It rescans hourly and on demand. If no clone is found, there is no
   auto-run and the button is shown as "No local clone".
2. **WorktreeManager** runs `git -C <clone> fetch origin <ref>` and then
   `git worktree add --detach <wt> FETCH_HEAD`. The ref is `pull/<n>/head`
   for GitHub and the PR's source branch for ADO. The worktree goes in
   `%LOCALAPPDATA%\GhNotify\worktrees\<tag>`. It is removed (`git worktree remove --force`)
   when the item leaves the inbox and no "Open in Claude" terminal was
   launched in the last 24 h. Otherwise it is removed 24 h after the item leaves.
3. **ClaudeRunner** runs in the worktree:
   ```
   claude -p "<prompt>" --output-format json
          --permission-mode default
          --allowedTools <config.agentAllowedTools>
          --disallowedTools "Bash(gh pr review:*)" "Bash(gh pr comment:*)" "Bash(gh pr merge:*)"
                            "Bash(gh api -X POST:*)" "Bash(gh api --method POST:*)"
                            "Bash(git push:*)" "Bash(git commit:*)" "Bash(az repos pr:*)" ...
   ```
   The prompt is the resolved profile's `prompt` with its placeholders filled in.
   The disallowed-tools list is a second line of defence behind the prompt.
   **Trust boundary:** it is best-effort, not airtight. For example,
   `gh api <path> -f x=y` POSTs without `-X`, and `Bash(bash:*)` (needed for
   the skills' scripts) can run anything. The real guarantees are the skills'
   own hard stops (peer-review never posts without approval) and the unattended
   prompt. Plan task 1 also checks the deny list against a scratch PR, and
   tightens `gh api` to read-only patterns where the Claude Code permission
   syntax allows it.
   The JSON output gives `session_id`, `result` (from which the SUMMARY line
   is extracted) and `total_cost_usd`.
4. At most `maxConcurrentAgents` (default 2) run at once; others queue. Each
   run has a timeout of `agentTimeoutMinutes` (default 20).
5. **Agent state per item:** `Queued`, `Running`, `Ready` (with summary and
   cost), `Failed` (with error tail), or `Outdated` (head SHA changed since the
   run). An auto rule does **not** re-run on new commits in v1; the row shows
   "Outdated — re-run".
6. When a run reaches Ready or Failed, a toast is shown (subject to work
   hours): "Pre-review ready: org/repo#123 — <summary>".

### Hand-off

"Open in Claude" runs
`wt.exe -d <worktree> claude --resume <session_id>`, falling back to
`powershell -NoExit -Command claude --resume <id>` if Windows Terminal is
missing. Resume has to run in the worktree directory because Claude sessions
are stored per working directory. From there I continue normally, for example
approving which findings `/peer-review` posts.

## 8. UI

- **Tray icon** with the count drawn on it. Grey = all sources healthy and 0
  items; accent colour = items waiting; small warning dot = a source is
  unhealthy.
- **Popup** (left-click), about 420×560, anchored to the tray, closes on lost
  focus:
  - Sections in this order: *Approvals*, *Review requests*, *My PRs — failing*,
    *My PRs — needs response*. Empty sections are hidden.
  - Row: provider glyph · repo · title (one line, ellipsis) · age · agent
    badge. Click the row → open the URL. Row buttons: `Pre-review`/`Shepherd`
    (when the rule is button, or to re-run), `Open in Claude` (when Ready or
    Failed). Hovering the badge shows the summary.
  - Footer: last refreshed time, source problems (e.g. "ADO:contoso — run az
    login"), `Refresh`, and a `⋯` menu with Open config, Start with Windows
    (HKCU Run key) on/off, and Quit.
- No settings UI in v1: "Open config" opens
  `%APPDATA%\GhNotify\config.json` in the default editor, and the file is
  hot-reloaded on save.

## 9. Config (`%APPDATA%\GhNotify\config.json`)

```jsonc
{
  "pollSeconds": 120,
  "workHours": { "days": ["Mon","Tue","Wed","Thu","Fri"], "start": "08:00", "end": "18:00" },
  "github": { "enabled": true, "includeDrafts": false, "watchRepos": [] },
  "ado": { "enabled": true, "orgs": [], "discoverOrgs": true, "excludeOrgs": [], "includeDrafts": false },
  "repoRoots": ["C:\\dev"],
  "maxConcurrentAgents": 2,
  "agentTimeoutMinutes": 20,
  "agentAllowedTools": ["Read", "Grep", "Glob", "Bash(gh pr view:*)", "Bash(gh pr diff:*)", "Bash(gh api:*)", "Bash(git:*)", "Bash(az repos:*)", "Bash(bash:*)"],
  "rules": [],
  "agentProfiles": {},   // merged over the built-in peer-review + pr-shepherd profiles
  "hooks": []
}
```

A default file is written on first run. If the config is invalid, the last
good config stays in effect and the footer shows the parse error.

## 10. Error handling

| Failure | Behaviour |
|---|---|
| `gh`/`az` missing or logged out | Source unhealthy, its items kept as stale, footer explains the fix; retried every poll |
| HTTP 401 | Refresh the token once, then unhealthy |
| HTTP 403/429 secondary rate limit | Back off for that source (respect `Retry-After`, else 5 min); no removals |
| One ADO project fails | Skipped this poll and logged; the other projects still report |
| `claude` not on PATH | Agent buttons disabled with a tooltip |
| Worktree/fetch failure | Agent → Failed with the git error |
| App crash | Log at `%LOCALAPPDATA%\GhNotify\logs\` (rolling, 7 days); state survives restart |

## 11. Testing

- **Core:** hook dispatch (event + match → process args, stdin JSON, env vars, timeout), profile resolution and placeholder filling, reconciler (added/removed/changed, unhealthy source keeps items,
  first-poll summary), work-hours window (including overnight ranges), rules
  matching (first match wins, globs), config parse and fallback.
- **Providers:** a fake `HttpMessageHandler` serving recorded JSON fixtures
  for every call in §5/§6, asserting the item set (including team requests,
  drafts excluded, `current_user_can_approve == false` excluded, ADO group
  reviewer, vote ≠ 0 excluded).
- **Agents:** URL normalisation table, RepoLocator against a temp directory of
  `git init`'d repos, ClaudeRunner through an `IProcessRunner` fake (argument
  list includes the disallowed tools; SUMMARY extraction; timeout → Failed).
- **App:** manual smoke checklist (badge, popup, toast, toast removal on
  resolve, Open in Claude) recorded in the plan.

## 12. Out of scope for v1

- Approving deployments or PRs from the app or a toast.
- GitHub Enterprise Server and other forges.
- Snooze/mute per item. A settings UI. Installer/MSIX (run from the publish
  folder, with an optional Start-with-Windows toggle).
- Auto re-running agents on new commits.
- A built-in agent profile for ApprovalPending items (custom profiles can target them).
