# gh_notify on macOS

gh_notify runs in the menu bar on macOS 13 (Ventura) or later, on Apple Silicon and Intel Macs. It works the same way as on
Windows, and updates itself the same way.

## Install

1. Download the installer for your Mac from the [latest release](https://github.com/JoshHambright/gh_notify/releases/latest):
   - Apple Silicon (M1 and later): `GhNotify-osx-arm64-Setup.pkg`
   - Intel: `GhNotify-osx-x64-Setup.pkg`. Intel builds pass CI but haven't been tried on a real Intel Mac yet.

   Not sure which you have? Apple menu → About This Mac: **Chip** means Apple Silicon, **Processor** means Intel.
2. Open the `.pkg` and follow the installer.
3. Start gh_notify from Applications. gh_notify isn't notarized by Apple yet, so the first time macOS says it can't check the
   app for malicious software. Open System Settings → Privacy & Security, scroll down, and click **Open Anyway** next to
   gh_notify. You only do this once; updates open normally.
4. When macOS asks whether gh_notify may send notifications, click **Allow**. You can change this later in System Settings →
   Notifications → gh_notify.
5. Sign in to GitHub once: `gh auth login`. Install the [GitHub CLI](https://cli.github.com/) with `brew install gh` if you
   don't have it.
6. Optional, for Azure DevOps: `brew install azure-cli`, then `az login`.

gh_notify finds `gh`, `az` and `claude` in Homebrew's folders and on your shell's PATH, even when it starts at login.

## Using it

- Click the menu-bar icon for its menu, then **Open** to show the popup (or press the hotkey). The icon is plain when nothing
  is waiting, and red with a count when something is. The menu also has Settings…, What's new, Check for updates and Quit.
- **Ctrl+Option+G** opens and closes the popup from anywhere. Change it in Settings › General (`hotkey`). You can write
  `Cmd` and `Option` in `config.json`.
- **Open at login** is in Settings › General. If macOS asks you to approve it, allow gh_notify in System Settings → General →
  Login Items.
- Agent actions such as **Open in Claude** open in Terminal.app. To use iTerm2 or another terminal, change Settings › Advanced
  › **Open terminals in** (`display.terminal`, see [Configuration](configuration.md#display)). The first time,
  macOS asks whether gh_notify may control Terminal: click **OK**. If you declined, allow it in System Settings → Privacy &
  Security → Automation.

## Where things live

| What | Where |
|---|---|
| Settings | `~/Library/Application Support/GhNotify/config.json` |
| Snapshot, mutes, snoozes | `~/Library/Application Support/GhNotify-data/` |
| Logs | `~/Library/Logs/GhNotify/` (also in Console.app) |

## Uninstall

macOS has no uninstaller for gh_notify. To remove it:

1. Turn off Open at login in Settings › General, or remove gh_notify from System Settings → General → Login Items.
2. Quit it: menu-bar icon → **Quit**.
3. Drag gh_notify from Applications to the Trash.
4. Optionally delete `~/Library/Application Support/GhNotify`, `~/Library/Application Support/GhNotify-data` and
   `~/Library/Logs/GhNotify`.
