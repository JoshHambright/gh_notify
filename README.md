# gh_notify

A small Windows tray app that shows everything on GitHub and Azure DevOps waiting on you:

- review requests
- pipeline and deployment approvals
- your PRs with failing CI
- your PRs that need a response

It toasts new items during work hours. When something is merged, approved or
voted on, it disappears from the inbox, and its toast is removed from Action
Center.

It can also pre-run Claude Code agents, so a review is already done by the time you open it.

## Requirements

- Windows 10/11, .NET 10 SDK
- `gh` logged in (`gh auth login`). Used for GitHub.
- `az` logged in (`az login`). Used for Azure DevOps. One login covers every org.
- Optional: `claude` (Claude Code) on PATH, for agents.

## Run

```
dotnet run --project src/GhNotify.App
```
or publish it with `dotnet publish src/GhNotify.App -c Release -o publish` and run `publish\GhNotify.exe`.
Use the popup's **⋯ → Start with Windows** to launch it at login.

## Screenshots

Render the popup (dark and light, each state) and the tray icon to PNGs from built-in sample data:

```
GhNotify.exe --render-preview <dir>
```

It uses placeholder data only (contoso / fabrikam / acme), never your accounts, and exits when done.

## Using the popup

Click the tray icon to open the popup. Opening or focusing it refreshes the inbox, unless a poll finished in the last 10 seconds.

- **Title:** opens the PR, run or approval in the browser.
- **Chevron (⌄):** expands the row in place. The details show the full repo name, author, branch, commit,
  environment or pipeline, and, if an agent ran, its profile, state, cost, duration and full result text,
  with **Copy** and **Open in Claude**. Only one row is open at a time.
- **Approvals:** approvals get **Approve…** and **Reject…**. Review requests get **Approve PR…**. Your own PRs get none.
  Every action takes two steps:
  1. The first click checks the live state and shows exactly what will happen, such as
     "Approve deployment of acme/deploy run 9001 to production". A PR without a pre-review at its current commit shows
     a yellow "No pre-review at this commit." warning. You can add an optional one-line comment.
  2. The second click, on a button that names the target (**Approve production**, **Approve PR**), sends it.
     This button stays disabled for the first 600 ms, so a double-click can't go through both steps.

  The app checks the state again just before sending. If anything changed (new commits, approval already handled),
  it refuses and asks you to refresh. Actions use your own `gh` / `az` login. Agents never trigger them.
- **Footer:** shows the last update time and the health of each source. A red ✗ means a source failed, and its tooltip
  says why. The buttons are ↻ (refresh), ☾ (theme) and ⋯ (menu).
- **Theme:** ☾ cycles System → Dark → Light. System follows the Windows app theme. The choice is saved in `state.json`.

## Config

`%APPDATA%\GhNotify\config.json` is created on first run and reloaded when you save it.
**⋯ → Open config** opens it.

```jsonc
{
  "pollSeconds": 120,
  "workHours": { "days": ["Mon","Tue","Wed","Thu","Fri"], "start": "08:00", "end": "18:00" },
  "github": { "enabled": true, "includeDrafts": false, "watchRepos": [] },
  "ado": { "enabled": true, "orgs": [], "discoverOrgs": true, "excludeOrgs": [], "includeDrafts": false, "azSubscription": null, "azTenant": null },
  "repoRoots": ["C:\\dev"],
  "rules": [
    { "match": { "author": "dependabot[bot]" }, "agent": "none" },
    { "match": { "repo": "my-org/core-*", "kind": "ReviewRequested" }, "agent": "auto" },
    { "match": { "kind": "MyPrCiFailing" }, "agent": "auto" }
  ],
  "agentProfiles": {
    "deploy-check": { "prompt": "/post-deploy-verify {url} — report only. End with \"SUMMARY:\".", "needsWorktree": false, "requiresSkill": "post-deploy-verify" }
  },
  "hooks": [
    { "event": "ItemAdded", "match": { "kind": "ApprovalPending" }, "run": ["pwsh", "-File", "C:\\scripts\\ping-teams.ps1"] }
  ],
  "display": { "repoAliases": { "contoso-engineering": "contoso" }, "maxRepoChars": 34 }
}
```

`display.repoAliases` shortens repo names in the popup. A key is an org (`"contoso-engineering"`) or an
org/project prefix (`"contoso-engineering/platform"`). The longest matching prefix is replaced by the value.
A name still longer than `display.maxRepoChars` (default 34, minimum 12) is shortened in the middle. The repo name
itself is kept, for example `contoso-engi…/checkout-service-api`. The full name is in the tooltip and in the expanded details.

`ado.azSubscription` / `ado.azTenant` (optional): use when your default `az` account is not the one in Azure DevOps. The token is requested with `--subscription <name-or-id>` (preferred) or `--tenant <id>`.

Most changes apply when you save. A change to `maxConcurrentAgents` needs a restart: quit from the tray and start the app again.

## Agents and skills

The app ships **no skills**: bring your own. The built-in profiles call `/peer-review` (for review requests) and
`/pr-shepherd` (for your red PRs). A skill counts as installed if its `SKILL.md` is in one of these places:

- `~/.claude/skills/<name>/`
- an installed Claude Code plugin

Skills in the repo's own `.claude/skills/` do not count; see below.

If a skill is missing, the row's button is disabled and its tooltip says why.

Agents run unattended in a throwaway git worktree of your local clone, found under `repoRoots`.
They are told not to post, push or comment, and a deny list blocks the usual commands for that.
The worktree holds the PR's own code, so runs pass `--setting-sources user`: the PR's `.claude/settings.json`
(hooks, permission rules) never applies. That also stops Claude loading the repo's `.claude/skills/`, so keep the skills
you use in `~/.claude/skills/` or a plugin.
When a run finishes, the row shows **✓ Ready** and a toast appears.
**Open in Claude** resumes that exact session in Windows Terminal, so you can approve what gets posted.

## Hooks

Each hook gets the item as JSON on stdin, plus these environment variables:
`GHN_EVENT`, `GHN_KEY`, `GHN_URL`, `GHN_KIND`, `GHN_REPO`.
The events are `ItemAdded`, `ItemRemoved`, `ItemChanged` and `AgentFinished`.

## Files

- Config: `%APPDATA%\GhNotify\config.json`
- State: `%LOCALAPPDATA%\GhNotify\state.json`
- Logs: `%LOCALAPPDATA%\GhNotify\logs\`
- Agent worktrees: `%LOCALAPPDATA%\GhNotify\worktrees\`
