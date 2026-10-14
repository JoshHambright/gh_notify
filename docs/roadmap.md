# Roadmap

gh_notify answers one question: **what is waiting on me right now?** Each release below helps answer it faster or lets you act on the answer without leaving the tray.

Every release follows the same pipeline: a design spec (`docs/superpowers/specs/`), then an implementation plan (`docs/superpowers/plans/`), then a feature branch, PR, release-please release and auto-update. A release does not start until the one before it has shipped.

| Release | Theme | Status |
|---|---|---|
| v1.0–v1.2 | Inbox, toasts, approvals, agents, installer, settings, mute/snooze | ✅ Shipped |
| **v1.3** | Clear work from the tray | 🟡 In progress |
| **v2.0** | Cross-platform UI (Avalonia), Windows first | 🟡 Designing ([spec](superpowers/specs/2026-10-14-gh-notify-v2.0-design.md)) |
| **v2.1** | macOS | ⚪ Planned, [#1](https://github.com/JoshHambright/gh_notify/issues/1) |
| **v2.2** | Broader inbox and smarter alerts | ⚪ Planned |
| **v2.3** | Agent hand-off and insights | ⚪ Planned |

Not planned for now: code signing, and Teams/Slack notifications. Revisit them after v2.3. Workflow profiles and hooks can already post to chat tools today.

---

## v1.3: Clear work from the tray

**Goal:** the common "it's waiting on me" items can be finished from the popup, not only seen there.

| Feature | What it does |
|---|---|
| **Merge from the tray** | A two-step **Merge…** on "Ready to merge" rows. It uses the same safety as approvals: a live re-check, a statement of what will happen, and the 600 ms confirm guard. You choose the merge method (merge, squash or rebase, limited to what the repo allows) and whether to delete the branch. On GitHub and ADO, the merge only runs if the head commit is still the one you saw. |
| **Re-run failed checks** | **Re-run failed jobs** on "CI failing" rows (GitHub Actions), and **Queue again** for ADO builds. Two-step. |
| **Batch approvals** | Select several approval rows and approve or reject them with one confirm. The confirm lists every run and environment; each is re-checked live, and results are reported per item. |
| **Keyboard first** | A global hotkey opens the popup (default Win+Shift+G, configurable). ↑/↓ move between rows, Enter opens, Space expands, Esc closes, and per-action keys start the two-step confirm. A search box (Ctrl+F or just typing) filters rows by repo, title, number or author. |

**Builds on:** `IItemActions` with its preview/execute tokens, `RowStateMachine`, PopupWindow and InboxPresenter.
**Risks:** branch-protection rules (required reviews, merge queue) differ per repo, so the preview must report GitHub's merge blockers rather than predict them. The global hotkey can clash with other apps, so registration failure is surfaced in Settings.
**Exit criteria:** merge, re-run and batch approval work end to end on GitHub and ADO; every action has unit tests for preview/execute token mismatches and stale results; the popup is fully usable without a mouse.

## v2.0: Cross-platform UI, Windows first

**Goal:** the same app on a UI that can run on macOS, shipped to Windows first with no regressions.

- Port the WPF popup, settings window (six pages), menus and themes to **Avalonia**, keeping the same layout, copy and safety rules.
- Move Windows-only behaviour into `GhNotify.Platform.Windows`, behind interfaces: toasts, hotkey, startup, sounds, theme detection, shell.
- Add headless UI tests (Avalonia.Headless) for the keyboard, search, batch and confirm-guard flows.
- Add a Linux CI job that proves the UI is platform-neutral.
- Users get v2.0 as a normal auto-update from v1.3. CI adds a v1.3 → v2.0 upgrade test.

**Exit criteria:** the parity checklist is ticked, screenshots match, and the build has been dogfooded for a few days before release.

## v2.1: macOS

**Goal:** the v2.0 app on macOS. There is no Mac on hand, so CI does the verification and a borrowed or cloud Mac runs the final smoke test.

| Area | Plan |
|---|---|
| **Tray** | Avalonia `TrayIcon` as the menu-bar item (template image, calm → red). |
| **Notifications** | Windows keeps toasts. macOS uses `UNUserNotificationCenter` through a small native shim, with action buttons where supported. |
| **Sounds** | `afplay` on macOS; the built-in wavs are reused. |
| **Startup** | A Run key on Windows; a LaunchAgent plist on macOS. |
| **Auth** | `gh auth token` and `az` work on both. Any stored secrets go to Keychain on macOS. |
| **Install/update** | Velopack `.pkg` for macOS (Apple Silicon and Intel), ad-hoc signed; `Setup.exe` for Windows. Separate release channels per OS. Developer ID and notarization come later. |
| **Hotkey** | Carbon `RegisterEventHotKey` through `GhNotify.Platform.Mac`. |
| **CI** | Add a `macos-latest` job: build, headless UI tests and pack. |

**Builds on:** v2.0 platform interfaces; Core, GitHub, ADO and Agents are already platform-neutral.
**Risks:** ad-hoc signed apps need a one-time "Open Anyway" in System Settings → Privacy & Security, which the docs must explain. Notification permissions need a bundled `.app`. UI parity has to be checked screen by screen.
**Exit criteria:** feature parity with Windows; a macOS install, upgrade and uninstall have been verified on a real Mac.

## v2.2: Broader inbox and smarter alerts

**Goal:** more of what's waiting on you shows up, and the alerts get smarter about timing.

| Feature | What it does |
|---|---|
| **New item kinds** | Issues assigned to you; @mentions in PRs and issues; Dependabot and code-scanning alerts on repos you admin; ADO work items assigned to you. Each kind can be turned off, gets its own rules and sounds, and supports mute and snooze. |
| **More hosts** | GitHub Enterprise Server (host + `gh` auth per host) and multiple GitHub accounts, each a source with its own label. |
| **Aging and escalation** | Rows show their age. Past a threshold per kind (in work hours, e.g. review > 4h), a row turns amber, then red, and re-toasts once. |
| **Morning digest** | One summary toast at the start of work hours ("3 reviews · 1 approval · oldest 2d"), and optionally an end-of-day one. |
| **Focus-aware alerts** | Respect Windows Focus Assist and macOS Focus: toasts are held and summarized afterwards. **Snooze everything for 1h** from the tray menu. |

**Builds on:** the source → reconcile → NotificationPlanner pipeline, WorkHours, Snoozes and RulesEngine.
**Risks:** GraphQL rate limits as the number of queries grows, so polling should be batched per host. Mention noise needs sensible defaults (off for repos you don't watch).

## v2.3: Agent hand-off and insights

**Goal:** agent output turns into your action faster; spending stays bounded; you can see how your queue trends.

| Feature | What it does |
|---|---|
| **Agent hand-off** | From an agent result: **Copy as review comment**, or **Open draft review** in the browser, pre-filled. You are always the one who posts, so the no-posting safety rule still holds. |
| **Cost guardrails** | A daily spend cap and per-repo limits for auto-run agents. When a cap is hit, auto-runs pause and the popup says why. Settings shows today's and the month's spend. |
| **Insights page** | Review turnaround, how long approvals wait, PRs merged per week, and the oldest waiting items, with local history only (nothing leaves the machine). |

**Builds on:** AgentCoordinator results, StateStore, the v2.0 Avalonia UI (so the insights page is built once).
**Risks:** storing history means designing for size and retention (rolling 90 days); cost numbers depend on what the Claude CLI reports.

---

## Backlog (unscheduled)

- Code signing (Azure Trusted Signing): removes the SmartScreen and Gatekeeper warnings.
- Teams/Slack/webhook presets for notifications.
- GitLab and Bitbucket sources.
- Export/import settings.
- A `gh notify` CLI extension.
- Carry-over minors from v1.2: Enter on "Custom file…", live config-error bar, SoundPlayer disposal, tests for the shutdown path, a tag-on-main check in release.
