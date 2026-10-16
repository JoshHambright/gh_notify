# Testing gh_notify on a Mac

Thanks for helping. gh_notify is a tray app that shows the GitHub and Azure DevOps items waiting on you. It is moving from a Windows-only UI to Avalonia so it can run on macOS.

**Where things stand:** round 1 checks that the shared UI builds, passes its tests and renders correctly on macOS. Round 2 installs the v2.1 macOS app and walks through it.

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
git checkout main
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

## Round 2: install and walk through the app

This round installs the real app and checks the parts only a Mac can show: the menu-bar icon, notifications, the hotkey and
open at login. If `gh` is signed in (`gh auth login`), the inbox shows your real GitHub items, which are read only; without it the inbox
is empty and a few questions don't apply. It takes about 15 minutes.

### Get the package

1. Open the pull request we send you (for v2.1 that is [#16](https://github.com/JoshHambright/gh_notify/pull/16)) and click
   **Checks**, then the **CI** run, then scroll to **Artifacts**. You need to be signed in to GitHub to download it.
2. Download **gh_notify-macos-arm64** and unzip it. Inside is a `.pkg` file.

### Steps

In Terminal, from the repo folder. Until v2.1 is merged, the round 2 script lives on the `feat/v2.1` branch:

```sh
git fetch && git checkout feat/v2.1 && git pull
./build/mac-test.sh --round 2 ~/Downloads/gh_notify-macos-arm64/GhNotify-osx-arm64-Setup.pkg
```

No artifact, or prefer to build it yourself? With the Xcode command line tools (`xcode-select --install`) and vpk
(`dotnet tool install -g vpk --version 1.2.161`):

```sh
./build/pack-mac.sh 0.0.1-local out osx-arm64
./build/mac-test.sh --round 2 out/GhNotify-osx-arm64-Setup.pkg
```

The script:
1. installs the package (macOS asks for your password);
2. starts gh_notify (with `open`, like Finder) with settings in a scratch folder, so a real gh_notify setup is never touched;
3. shows a test notification after about 5 seconds, and opens a Terminal window after about 10;
4. asks you 13 yes/no questions, one at a time, each with room for a note.

The app is ad-hoc signed, not notarized. If macOS blocks it, open System Settings → Privacy & Security, click **Open Anyway**,
and say so in the first answer, including the exact wording macOS used.

### What to send back

Send back the one file `mac-test-r2-<time>.zip` from the repo folder. It contains your answers, `summary.json` (macOS version,
chip, app version and signature type) and the app's logs, with your home folder shown as `~`.

gh_notify stays installed afterwards. The script prints how to remove it.
