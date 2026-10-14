# gh_notify

A small Windows tray app that shows everything on GitHub and Azure DevOps that's waiting on you — and clears it the moment it isn't.

[![CI](https://github.com/JoshHambright/gh_notify/actions/workflows/ci.yml/badge.svg)](https://github.com/JoshHambright/gh_notify/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/JoshHambright/gh_notify)](https://github.com/JoshHambright/gh_notify/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/JoshHambright/gh_notify/total)](https://github.com/JoshHambright/gh_notify/releases)
[![License: MIT](https://img.shields.io/github/license/JoshHambright/gh_notify)](LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)

<p align="center"><img src="docs/images/hero-dark.png" alt="The gh_notify popup with one row expanded" width="380"></p>

| Dark | Light |
|---|---|
| ![Popup, dark theme](docs/images/popup-dark.png) | ![Popup, light theme](docs/images/popup-light.png) |

## What it watches

- review requests
- deployment and pipeline approvals
- your PRs failing CI
- your PRs needing a response
- your PRs ready to merge
- your other open PRs, as a quiet "Waiting on others" list

## Quickstart (60 seconds)

1. Download `GhNotify-win-Setup.exe` from the [latest release](https://github.com/JoshHambright/gh_notify/releases/latest).
2. SmartScreen: **More info → Run anyway** (the build is unsigned; see the [FAQ](#faq)).
3. Run `gh auth login`.
4. Optional: run `az login` for Azure DevOps.
5. Click the tray icon.

It starts with Windows and updates itself.

## Tour

### Two-step approvals

Approve or reject a deployment, pipeline approval or PR without leaving the tray. The first click checks the live state and
spells out exactly what will happen. The second click, on a button that names the target, sends it.

![Confirm panel for a deployment approval](docs/images/approve.png)

### Pre-review and diagnose agents

Claude Code agents can run unattended in a throwaway worktree: **Pre-review** reads a review request, **Diagnose** works out
why your PR is red. They are read-only, and a finished run shows **✓ Ready** with a one-line summary. The built-in
profiles need no skills; bring your own skills or scripts if you want. See [docs/workflows.md](docs/workflows.md).

### Sounds and off-hours alerts

Give any kind of item its own sound (five built-in tones or your own `.wav`). Outside work hours nothing toasts, unless a
sound rule says to alert off-hours, which is handy for approvals. One sound plays per poll, for the most important item.
See [docs/notifications.md](docs/notifications.md).

### Mute, hide and snooze

**Mute** a repo, org or project (silent but still listed), **hide** it entirely, or **snooze** a single item until it changes.
The row's **⋯** menu does timed mutes and snoozes in two clicks.

![Row menu with mute and snooze options](docs/images/filters.png)

### The settings window

Everything in `config.json` has a visual editor: sources, sound rules, filters, workflows and profiles, hooks.
Raw JSON stays one click away.

![Settings, Workflows page](docs/images/settings-workflows.png)

![Settings, Notifications page](docs/images/settings-notifications.png)

### The calm / red tray icon

No red when nothing is waiting. A red sweep and a red count appear the moment something is.

| Nothing waiting | Something waiting |
|---|---|
| ![Calm tray icon](docs/images/tray-calm.png) | ![Tray icon with a count](docs/images/tray-waiting.png) |

## FAQ

**Why does Windows warn when I install it?**
The installer isn't code-signed yet, so SmartScreen shows "Windows protected your PC". Click **More info → Run anyway**.
Releases are built by CI from tagged source; see [docs/security.md](docs/security.md).

**Azure DevOps shows ✗ or an AADSTS error.**
Your default `az` account probably isn't the one Azure DevOps uses. Set the subscription in
**Settings › Sources › Test ADO sign-in**, which shows ✓ or the fix. See [docs/troubleshooting.md](docs/troubleshooting.md).

**An item didn't clear.**
Items clear on the next poll after the provider reports them done. **Refresh** (↻) forces a poll. GitHub's search index can lag
by about a minute after a merge or review.

**Is approving from the tray safe?**
Every action takes two steps, the state is re-checked live just before sending, and the confirm button is disabled for 600 ms
after it appears. If anything changed, it refuses. Details in [docs/security.md](docs/security.md).

**What can the agents do?**
They are read-only: they can't comment, review, merge, push, commit or run pipelines. See [docs/security.md](docs/security.md).

**Where are my settings, logs and state?**
Config: `%APPDATA%\GhNotify\config.json`. State and logs: `%LocalAppData%\GhNotify-data`. The app itself is in
`%LocalAppData%\GhNotify`. **Settings › Advanced** has buttons that open each.

**How do I uninstall completely?**
Uninstall *GhNotify* from Windows **Apps & features**. That removes the app, shortcuts and the start-up entry, and keeps your
data. To remove that too, delete `%APPDATA%\GhNotify` and `%LocalAppData%\GhNotify-data`.

**Can I use my own skills or scripts?**
Yes. Map item kinds to your own profiles with `defaultProfiles`, or add rules. A profile is either a Claude prompt (which can
invoke your skills) or a command that runs any script. See [docs/workflows.md](docs/workflows.md).

**Toasts don't appear.**
Check Windows Focus Assist / Do Not Disturb. Also, toasts are off outside your work hours unless a sound rule sets
`alertOffHours`. See [docs/notifications.md](docs/notifications.md).

## Docs

- [Configuration](docs/configuration.md): every `config.json` key, type and default
- [Workflows](docs/workflows.md): profiles, rules, command scripts, hooks
- [Notifications](docs/notifications.md): work hours, sounds, mute, hide, snooze
- [Security](docs/security.md): tokens, agent limits, approvals, updates
- [Troubleshooting](docs/troubleshooting.md)
- [Development](docs/development.md): build, test, quality gates, releases

## Build from source

```powershell
dotnet build GhNotify.slnx -c Release
dotnet test GhNotify.slnx -c Release
dotnet run --project src/GhNotify.App
```

See [docs/development.md](docs/development.md).

## License

MIT — see [LICENSE](LICENSE). Bundled Roboto fonts are Apache-2.0 (see `src/GhNotify.App/Fonts/LICENSE.txt`).
