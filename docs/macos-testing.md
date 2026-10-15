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
# 1. Get the code
git clone https://github.com/JoshHambright/gh_notify.git
cd gh_notify
git checkout feat/v2.0

# 2. Record your setup
sw_vers; uname -m; dotnet --info > dotnet-info.txt

# 3. Build the shared UI and its tests (warnings count as errors, same as CI)
dotnet build tests/GhNotify.UI.Tests/GhNotify.UI.Tests.csproj -c Release -warnaserror

# 4. Run the UI tests headless (no windows appear)
dotnet test tests/GhNotify.UI.Tests/GhNotify.UI.Tests.csproj -c Release --no-build \
  --logger "trx;LogFileName=ui.trx" --results-directory test-results

# 5. Run the core logic tests (provider rules, config, actions)
dotnet test tests/GhNotify.Tests/GhNotify.Tests.csproj -c Release \
  --logger "trx;LogFileName=core.trx" --results-directory test-results

# 6. Render the screenshots on macOS
mkdir -p mac-screenshots
GHNOTIFY_SCREENSHOTS="$PWD/mac-screenshots" \
  dotnet test tests/GhNotify.UI.Tests/GhNotify.UI.Tests.csproj -c Release --no-build \
  --filter "Category=Screenshots"
ls mac-screenshots
```

If a step fails, **keep going with the next one**. Don't try to fix the code. The failure is what we want to see.

### What to look at

1. **Steps 3 and 4:** these should pass with zero errors and zero failed tests. Linux CI already runs them, so a failure on macOS is a real macOS finding.
2. **Step 5:** some of these tests may assume Windows paths. Failures here help us scope v2.1, so just report them.
3. **Step 6:** open the PNGs in `mac-screenshots/` and compare them with the Windows ones in `docs/images/` (same file names). Look for:
   - text that is cut off, overlaps or wraps differently
   - a wrong or fallback font (it should match the Windows images, not Helvetica or Times)
   - missing icons or glyphs, such as ⋯, chevrons or the empty boxes shown for unknown characters
   - colours that differ between light and dark
   - anything that looks blurry. The images are rendered at 2×.

### What to send back

- The output of step 2 (`sw_vers`, `uname -m`) and `dotnet-info.txt`
- The `test-results/` folder, zipped
- The `mac-screenshots/` folder, zipped
- The full terminal output of any step that failed. Copy and paste it; a screenshot isn't enough.
- A few lines on anything that looked off in the screenshots

Everything here uses placeholder names only (contoso, fabrikam, alice, bob), so it's safe to share.

## Round 2: install and smoke test (when v2.1 is ready)

We'll send a `.pkg` and a short checklist covering:

- **Installing:** the app is ad-hoc signed, so macOS will block the first launch. You'll allow it once in System Settings → Privacy & Security → **Open Anyway**. We want to know exactly what you saw.
- **Menu-bar icon:** it is calm when nothing is waiting and red with a count when something is. Clicking it opens the popup below the icon.
- **Notification permission prompt:** the notifications appear and clicking one opens the right item.
- **Sign-in:** reusing `gh auth login`, the global hotkey, sounds and start at login.
- **Updates and uninstall:** updating to a newer build keeps your settings, and uninstalling cleans up.

That round needs a GitHub account with `gh` installed (`brew install gh`). We'll send exact steps then.
