# Troubleshooting

Logs are in `%LocalAppData%\GhNotify-data\logs\` (**Settings › Advanced › Open logs folder**). A red ✗ in the popup footer
means a source failed; hover it for the reason.

| Symptom | Likely cause | Fix |
|---|---|---|
| GitHub shows ✗ | `gh` isn't installed, isn't on `PATH`, or isn't logged in, or the token expired. | Run `gh auth status`; if needed `gh auth login`. Restart the app if you just installed `gh`. Click ↻. |
| Azure DevOps shows ✗ | Not signed in with `az`, or the token request failed. | Run `az login`, then click ↻. |
| ADO shows `AADSTS500011` or `AADSTS50020` | Your default `az` account or tenant isn't the one that owns the Azure DevOps org (guest account, or another tenant). | Set `ado.azSubscription` (preferred) or `ado.azTenant`: **Settings › Sources**, then **Test ADO sign-in**, which shows ✓ or the fix. |
| Items missing from one ADO org | The org wasn't discovered, or is in `ado.excludeOrgs`. | **Settings › Sources**: tick the org or **Add org**. |
| An item didn't clear | It clears on the first poll after the provider reports it done; GitHub's search index can lag about a minute. | Click ↻ (Refresh). If it's still there after a couple of minutes, check it is really merged, approved or closed. |
| Agent button says "No local clone under repoRoots" | No clone of that repo was found. | Clone the repo under one of `repoRoots`, or add the folder that holds it (**Settings › Sources › Repo roots**). The clone's `origin` remote must match the PR's repo. |
| Agent button says "Skill /name not installed" | The profile has `requiresSkill` and no `SKILL.md` was found in `~/.claude/skills/<name>/` or an installed plugin. (A repo's own `.claude/skills/` doesn't count.) | Install the skill, or use a built-in profile that needs none (`review`, `diagnose-ci`). |
| Agent button says "'name' not found on PATH" | A command profile (or `claude`) isn't on `PATH` for the app. | Install it, or give the full path in `command`. The app inherits `PATH` from when it started, so restart it after changing `PATH`. |
| An agent run fails with "exit N" or "timed out" | Command profile returned a non-zero exit code, or the run exceeded `agentTimeoutMinutes`. | Expand the row to read the output (last 64 KB for commands). Raise the timeout if needed. |
| A `.cmd` command profile fails with "argument contains …" | The filled arguments contain `"`, `%`, `!` or a line break, which can't be passed safely to a batch file. | Use `pwsh -File script.ps1` or an `.exe`, or read `GHN_TITLE` from the environment. |
| Toasts don't appear | Windows Focus Assist / Do Not Disturb is on; it's outside work hours (and no sound rule has `alertOffHours`); the item is muted, hidden or snoozed; or notifications are off for the app in Windows Settings. | Turn off Do Not Disturb, check **Settings › General** work hours and **Settings › Filters**, and Windows **Settings › System › Notifications**. |
| No sound | Sound is `none`, a custom `.wav` is missing, or only one sound plays per poll. | Check the sound rule; a bad path falls back to the default sound and logs a warning. See [notifications.md](notifications.md). |
| "Update check failed" (**Settings › General**) | Offline, behind a proxy, or GitHub rate-limited the request. | Nothing to do; it retries every `updates.checkHours`. Use **Check now** later. A dev build shows "Dev build — updates off". |
| Rate-limit errors or slow polls with many open PRs | The deployment-approvals scan covers `watchRepos` and also the repos of all your open PRs, so each poll makes more REST calls. | Raise `pollSeconds` or trim `watchRepos`. |
| Update downloaded but the version didn't change | Updates apply when the app restarts. | Click **Update ready · v<version> · Restart** in the popup footer, or **Restart to update (v<version>)** in the popup's **⋯** menu or the tray icon's right-click menu; otherwise it is applied the next time gh_notify starts. Menu: **Check for updates** forces a check. |
| The app doesn't start at login | The Windows start-up entry (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `GhNotify`) is missing or points at an old path, or start-up apps are disabled in Windows. | Turn **Start with Windows** off and on in **Settings › General**; check **Settings › Apps › Startup** in Windows. |
| Config banner "Fix in Settings" | `config.json` has invalid JSON or a bad value (for example `pollSeconds` under 30, or an unknown profile name). | Open **Settings**, which highlights the field, or edit `%APPDATA%\GhNotify\config.json`. The previous file is `config.json.bak`. |
| SmartScreen blocks the installer | The build is unsigned. | **More info → Run anyway**. |

Still stuck? Open an issue with the relevant lines from the log. Redact org and repo names first.
