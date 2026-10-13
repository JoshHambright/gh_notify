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
  ]
}
```

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
