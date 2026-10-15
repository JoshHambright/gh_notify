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

`GhNotify.exe --version` prints the version. The UI tests run headless (Avalonia.Headless), so they need no desktop session
and run on Windows and Linux alike.

## Screenshots

`build/screenshots.ps1` renders `docs/images/*.png` with Avalonia.Headless: the `Screenshots` test in `tests/GhNotify.UI.Tests`
draws the popup (from `SampleHost`) and the settings pages (from `SampleSettingsHost`) in light and dark, using placeholder names only.
The test does nothing unless `GHNOTIFY_SCREENSHOTS=<outdir>` is set, so a normal `dotnet test` skips it. To run it by hand:

```powershell
$env:GHNOTIFY_SCREENSHOTS = "$PWD/docs/images"; dotnet test tests/GhNotify.UI.Tests --filter Category=Screenshots
```

It writes `hero-dark.png`, `popup-dark.png`, `popup-light.png`, `approve.png`, `filters.png`, `settings-workflows.png`,
`settings-notifications.png`, `tray-calm.png`, `tray-waiting.png`, plus a `review/` folder of every popup state and settings page in
both themes (the script deletes it). Re-run it when the UI changes and commit the images.

## Quality gates

CI (`.github/workflows/ci.yml`) runs these on every push and PR on `windows-latest`. All are required. A second job on
`ubuntu-latest` builds `GhNotify.UI` and Core and runs `GhNotify.UI.Tests` headless; it proves the UI project has no Windows
dependency, so never reference a Windows API from `GhNotify.UI` (put it behind a platform interface instead).

| Gate | Run locally |
|---|---|
| Build, warnings as errors | `dotnet build GhNotify.slnx -c Release -warnaserror` |
| Tests | `dotnet test GhNotify.slnx -c Release` |
| Coverage floor | `dotnet test GhNotify.slnx -c Release --collect:"XPlat Code Coverage" --results-directory $env:TEMP\ghn-cov`, then `pwsh build/check-coverage.ps1 -Results $env:TEMP\ghn-cov` |
| Formatting | `dotnet format GhNotify.slnx --verify-no-changes` (drop the flag to fix) |
| Leak scan | `pwsh build/leak-scan.ps1 -TermsFile <path>` |
| Install / upgrade / uninstall | `build/test-install.ps1`, CI only (it installs real packages). Also upgrades from the latest published release's `GhNotify-win-Setup.exe` to the locally packed build and checks config, state, the Run key and the shortcut survive; skipped with a notice if no release exists |
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

## Commit messages

Use [Conventional Commits](https://www.conventionalcommits.org/): `feat: ...` (minor bump), `fix: ...` (patch bump),
`perf:`, `docs:`, and the hidden types `ci:`, `build:`, `test:`, `style:`, `chore:`, `refactor:`. Add `!` after the type or a
`BREAKING CHANGE:` footer for a major bump. The types drive both the version and the changelog.

## Release process

Releases are managed by [release-please](https://github.com/googleapis/release-please) (`.github/workflows/release-please.yml`,
configured by `release-please-config.json` and `.release-please-manifest.json`).

1. Merge work to `main` with conventional commit messages.
2. release-please keeps a pull request titled `chore(main): release X.Y.Z` open. It bumps `version.txt`, updates
   `CHANGELOG.md` and proposes the next version from the `feat:`/`fix:` commits since the last release.
3. Write the release notes (see [Release notes](#release-notes) below) and merge them to `main`. The release PR shows a red
   `release-notes` status until `docs/releases/vX.Y.Z.md` exists.
4. Merging the release PR creates the `vX.Y.Z` tag and GitHub release. The same workflow then
   calls `.github/workflows/release.yml`, which runs the CI gates, publishes self-contained `win-x64`, downloads the previous
   release (for delta packages), packs with `vpk` (`build/pack.ps1`), runs the install test against the real previous
   release, then merges `GhNotify-win-Setup.exe`, the full and delta packages and `releases.win.json` into that release a few
   minutes later, and replaces the release body with the full notes.

To re-run the packaging for an existing release, use Actions → Release → Run workflow and enter the tag (for example
`v1.2.0`).

Notes:

- The release PR itself gets no CI run. It only changes `CHANGELOG.md`, `.release-please-manifest.json` and `version.txt`,
  which `ci.yml` ignores on pull requests (GitHub would otherwise hold each run for manual approval, because the PR is pushed with
  `GITHUB_TOKEN`). Merging it triggers CI on `main` and the release.
- Until the Release workflow finishes (CI gates, pack and upgrade test; typically 15–30 minutes), the release exists without update files.
- If the publish job fails, recover with Actions → Release → Run workflow and the tag.
- Dependency bumps (`chore:`) don't cut releases.

One-time repository setting: Settings → Actions → General → "Allow GitHub Actions to create and approve pull requests" must
be on, or release-please cannot open its PR.

Installed apps pick the new release up within `updates.checkHours`. Signing is not set up yet; the release workflow marks
where it goes.

### Release notes

Every release has hand-written notes in `docs/releases/vX.Y.Z.md`. Copy `docs/releases/TEMPLATE.md`, ideally in the feature PR
that does the work. Use the version release-please will cut: `feat!` gives the next major, `feat` the next minor, and only
`fix` the next patch. The template has these sections:
- one or two sentences in user terms, and a screenshot
- Highlights
- New, Changed, Fixed
- Upgrade notes (new config keys and defaults, or "Nothing to do")
- Learn more

Write links relative to `docs/releases/`, for example `../actions.md#merge`, `../images/popup-dark.png` or `../../README.md`. They work
when the file is browsed on GitHub, and `build/release-notes.ps1` rewrites them to the release tag. That way the release page
links to the docs as they were at that version, and images point at the raw files.

`build/release-notes.ps1` assembles each release from three parts:
1. **Release page:** the notes file goes on top, followed by "All changes", which is release-please's changelog.
2. **Update package:** the same text is packed with `vpk --releaseNotes`.
3. **In the app:** after an update, the popup footer shows "Updated to vX · What's new" once, and the tray menu has a
   permanent **What's new** item. Both open `https://github.com/JoshHambright/gh_notify/releases/tag/vX`.

The gates:
- `release-please.yml` sets the `release-notes` status on the release PR.
- `release.yml` won't publish without the file. If a release was tagged without notes, merge them to `main` and re-run
  Actions → Release with the tag; the workflow reads the notes from `main`.
- `./build/release-notes.ps1 -Check` runs the same check locally, against `.release-please-manifest.json`.

## Project layout

```
src/
  GhNotify.Core      config, model, rules, filters, sounds plan, snoozes, settings draft, hooks, scheduling, state
  GhNotify.GitHub    GitHub source (GraphQL via gh), approvals and actions
  GhNotify.Ado       Azure DevOps source, discovery, approvals and actions
  GhNotify.Agents    Claude and command runners, worktrees, repo and skill locators, coordinator
  GhNotify.UI        Avalonia UI, platform-neutral (net10.0): App, popup, settings window and pages, themes, tray icon rendering,
                     AppController, row state machine, batch coordinator, sample hosts, and the platform interfaces it needs
  GhNotify.Platform.Windows  Windows implementations of those interfaces: toasts, global hotkey, start with Windows (Run key),
                     sounds, system theme, shell, install hooks and install info
  GhNotify.App       Windows host: Program (Velopack entry, --version, single instance), paths, update client, composition
tests/
  GhNotify.Tests     Core, GitHub, ADO and Agents unit tests
  GhNotify.UI.Tests  Headless Avalonia tests with fake platform services (also run on Linux CI), plus the screenshot renderer
  GhNotify.Platform.Windows.Tests  Windows-only tests (install info, ICO writer, hotkey registrar)
build/               pack, coverage, leak scan, screenshots and sound generation scripts
docs/                user guides; docs/superpowers has design specs and notes
```
