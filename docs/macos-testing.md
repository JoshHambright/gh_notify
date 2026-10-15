# Testing gh_notify on a Mac

Thanks for helping. gh_notify is a tray app that shows the GitHub and Azure DevOps items waiting on you. It is moving from a Windows-only UI to Avalonia so it can run on macOS.

**Where things stand:** v2.0 (branch `feat/v2.0`) ports the UI to Avalonia, but the app's startup host is still Windows-only. The macOS host, menu-bar icon, notifications and `.pkg` installer come in v2.1. So there is **no app to launch on a Mac yet.** Round 1 checks that the shared UI builds, passes its tests and renders correctly on macOS. Round 2, the real install test, comes when v2.1 is ready.

Each round takes about 15 minutes, plus download time.

## Round 1: build, test and render (now)

### What you need

- macOS 13 or later, on Apple Silicon or Intel. Tell us which one you used.
- The **.NET 10 SDK, version 10.0.400 or later**, from <https://dotnet.microsoft.com/download/dotnet/10.0>. Pick the macOS installer for your chip. Check it with `dotnet --version`.
- `git`. Running `xcode-select --install` provides it if it's missing.

You don't need a GitHub or Azure DevOps account, Xcode or an IDE. The tests use made-up sample data.

### Steps

Run these in Terminal:

```sh
git clone https://github.com/JoshHambright/gh_notify.git
cd gh_notify
git checkout feat/v2.0
./build/mac-test.sh
```

The script runs every check in order and keeps going if one fails. Please don't try to fix any failures: the failure itself is what we want to see. The steps are:

1. Build the shared UI with warnings treated as errors, the same as CI.
2. Run the UI tests headless. No windows appear.
3. Run the core logic tests.
4. Render the screenshots on macOS.

The whole run takes a few minutes. At the end the script stops and asks you to review the screenshots:

1. Open `mac-test-<time>/screenshots/`.
2. Compare each image with the file of the same name in `docs/images/`, which are the Windows versions. Look for:
   - text that is cut off, overlaps or wraps differently
   - a wrong or fallback font (it should match the Windows images, not Helvetica or Times)
   - missing icons or glyphs, such as ⋯, chevrons or empty boxes
   - colours that differ between light and dark
   - anything that looks blurry
3. Write what you see in `mac-test-<time>/notes.md`.
4. Press Enter.

### What to send back

Send back the one file `mac-test-<time>.zip` from the repo folder. It contains:
- `summary.json`: your macOS version, chip, .NET version, the exit code of each step, and test counts with the names of any failed tests.
- `logs/`: the full output of each step. Your home folder is shown as `~` in the logs.
- `test-results/`: the raw test results.
- `screenshots/` and your `notes.md`.

The UI build and UI tests should pass with no failures. Linux CI already runs them, so any failure there is specific to macOS. Some core tests may assume Windows paths; those failures help us scope v2.1. Everything uses placeholder names only (contoso, fabrikam, alice, bob), so the zip is safe to share.

## Round 2: install and smoke test (when v2.1 is ready)

We'll send a `.pkg` and a short checklist covering:

- **Installing:** the app is ad-hoc signed, so macOS will block the first launch. You'll allow it once in System Settings → Privacy & Security → **Open Anyway**. We want to know exactly what you saw.
- **Menu-bar icon:** it is calm when nothing is waiting and red with a count when something is. Clicking it opens the popup below the icon.
- **Notification permission prompt:** the notifications appear and clicking one opens the right item.
- **Sign-in:** reusing `gh auth login`, the global hotkey, sounds and start at login.
- **Updates and uninstall:** updating to a newer build keeps your settings, and uninstalling cleans up.

That round needs a GitHub account with `gh` installed (`brew install gh`). We'll send exact steps then.
