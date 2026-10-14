# Development

## Prerequisites

- Windows 10/11 and the .NET 10 SDK (`global.json` pins the SDK)
- `gh`, logged in (`gh auth login`)
- Optional: `az` (Azure DevOps) and `claude` (Claude Code, for agents)
- Optional: `vpk` for packaging: `dotnet tool install -g vpk --version 1.2.161`
- PowerShell 7 (`pwsh`) for the scripts in `build/`

## Build, test, run

```powershell
dotnet build GhNotify.slnx -c Release
dotnet test GhNotify.slnx -c Release
dotnet run --project src/GhNotify.App
```

Stop a running tray instance first; it locks the output folder. In a dev build updates are off ("Dev build — updates off").
`GHNOTIFY_CONFIG_DIR` and `GHNOTIFY_DATA_DIR` point a run at scratch folders so it never touches your real config or state.

`GhNotify.exe --version` prints the version. `GhNotify.exe --render-preview <dir>` renders the popup (dark and light, each state),
every settings page and the tray icon to PNGs from built-in sample data (placeholder names only), then exits. WPF pixel tests
and previews need an unlocked desktop session.

## Screenshots

`build/screenshots.ps1` builds the app, runs `--render-preview` into a temp folder and copies the chosen frames to `docs/images/`
(`hero-dark.png`, `popup-dark.png`, `popup-light.png`, `approve.png`, `filters.png`, `settings-workflows.png`,
`settings-notifications.png`, `tray-calm.png`, `tray-waiting.png`). Re-run it when the UI changes and commit the images.

## Quality gates

CI (`.github/workflows/ci.yml`) runs these on every push and PR on `windows-latest`. All are required.

| Gate | Run locally |
|---|---|
| Build, warnings as errors | `dotnet build GhNotify.slnx -c Release -warnaserror` |
| Tests | `dotnet test GhNotify.slnx -c Release` |
| Coverage floor | `dotnet test GhNotify.slnx -c Release --collect:"XPlat Code Coverage" --results-directory $env:TEMP\ghn-cov`, then `pwsh build/check-coverage.ps1 -Results $env:TEMP\ghn-cov` |
| Formatting | `dotnet format GhNotify.slnx --verify-no-changes` (drop the flag to fix) |
| Leak scan | `pwsh build/leak-scan.ps1 -TermsFile <path>` |
| Install / upgrade / uninstall | `build/test-install.ps1`, CI only (it installs real packages) |
| CodeQL | `codeql.yml`, weekly and on PRs |
| Dependency updates | Dependabot, weekly (NuGet and Actions) |

### Coverage floor

`build/coverage-floor.txt` holds the minimum line coverage in percent (currently 66). Coverage is a line-level union across all
test reports: a line counts as covered if any report hit it. Raise the floor whenever coverage goes up, in the same PR. Never lower
it without a reason in the PR description.

### Leak scan

`build/leak-scan.ps1` fails if any tracked text file or commit message contains one of a private list of terms (real org, client
and tenant names). In CI the terms come from the `LEAK_TERMS` secret. Locally, put one term per line in a file **outside the
repo** (or git-ignored) and pass `-TermsFile`. The terms are never committed, and hits print only `file:line`. Without terms the
scan is skipped with a notice. Use placeholder names (contoso, fabrikam, acme, alice, bob) everywhere.

## Release

1. Make sure `main` is green.
2. Tag with an **annotated** message; the message becomes the release notes:

   ```powershell
   git tag -a v1.2.0 -m "gh_notify 1.2.0`n`n- Installer and auto-update`n- Settings window`n- Sounds, mute, hide, snooze"
   git push origin v1.2.0
   ```

3. The pushed tag triggers `.github/workflows/release.yml`, which runs the CI gates, publishes self-contained `win-x64`,
   downloads the previous release (for delta packages), packs with `vpk` (`build/pack.ps1`), runs the install test against the
   real previous release, then uploads and publishes `GhNotify-win-Setup.exe`, the full and delta packages and
   `releases.win.json` to a GitHub Release.

Installed apps pick the new release up within `updates.checkHours`. The tag sets the version. Signing is not set up yet; the
release workflow marks where it goes.

## Project layout

```
src/
  GhNotify.Core      config, model, rules, filters, sounds plan, snoozes, settings draft, hooks, scheduling, state
  GhNotify.GitHub    GitHub source (GraphQL via gh), approvals and actions
  GhNotify.Ado       Azure DevOps source, discovery, approvals and actions
  GhNotify.Agents    Claude and command runners, worktrees, repo and skill locators, coordinator
  GhNotify.App       WPF tray app: popup, settings window, toasts, sounds, tray icon, Velopack install hooks and updates
tests/
  GhNotify.Tests     Core, GitHub, ADO and Agents unit tests
  GhNotify.App.Tests App tests (tray icon pixels, install info, action flow)
build/               pack, coverage, leak scan, screenshots and sound generation scripts
docs/                user guides; docs/superpowers has design specs and notes
```
