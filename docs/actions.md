# Actions and keyboard

gh_notify can finish the common "it's waiting on me" items from the popup. Every action uses your own `gh` / `az` login and
follows the same two-step rule: the first click checks the live state and says exactly what will happen, the second click, on a
button that names the target, sends it. See [security.md](security.md#in-app-approvals).

## Approvals

Approve or reject a deployment, a pipeline approval or a PR from its row. The confirm panel has an optional comment box.
If the item changed since you looked (new commits, already handled), the action is refused and the row refreshes.

## Merge

**Merge…** appears on "Ready to merge" rows, so only on your own PRs.

- **Only clean merges.** The preview reads the PR live and refuses if it isn't open, has new commits since you looked, or
  GitHub reports anything but a clean state (for example "Blocked by branch protection", "Behind the base branch",
  "Merge conflicts", "Some checks are not passing"). On Azure DevOps it also refuses on draft PRs and on rejected or running
  policies. A refusal shows the reason and **Open in browser**.
- **No admin bypass.** Branch protection and required policies are never overridden.
- **Method picker.** You choose merge, squash or rebase, limited to what the repo (or the target branch's merge-strategy policy
  on ADO) allows. The default is `actions.mergeMethod` if set and allowed, otherwise squash, then merge, then rebase.
- **Delete branch.** A checkbox appears only when the branch lives in the same repo as the PR; branches on forks are never
  deleted. If the merge works but the delete fails, the row says so ("Merged, but the branch couldn't be deleted").
- **Pinned to the commit you saw.** The merge is sent with that commit's sha. If the head moved, GitHub or Azure DevOps rejects
  it and the row shows "This changed since you looked — refreshing".

Example statement: "Squash and merge PR #12 into main in contoso/api at abc1234".

## Re-run failed

**Re-run failed…** appears on "CI failing" rows.

- **GitHub:** re-runs the failed jobs of the latest attempt of each failed GitHub Actions workflow at the PR's head commit.
  Checks that come from other systems are not touched; re-run those where they were reported.
- **Azure DevOps:** re-queues the failed build policies, the same as **Queue** on the PR page.

If some runs start and others don't, the row finishes with a warning naming the ones that failed. The row stays "CI failing"
until a later poll sees the new results.

## Batch approvals

When the Approvals section holds two or more rows you can approve, its header shows **Approve all…** and **Reject all…**.

1. Every row is previewed live; refused rows are unchecked and show why.
2. Untick anything you don't want, optionally add one shared comment, then confirm ("Approve 3").
3. Checked items are sent one at a time, in display order. Each line updates in place, and a summary follows, for example
   "3 approved · 1 changed since you looked · 0 failed".

While a batch is open, single-row actions in that section are disabled ("Finish the batch first"). Closing the panel before
you confirm cancels it.

## Keyboard shortcuts

### Global

| Keys | Does |
|---|---|
| Win+Shift+G | Show and focus the popup, or hide it. Change it with [`hotkey`](configuration.md#top-level); `""` turns it off. |

If another app already owns the hotkey, gh_notify keeps running and **Settings › General** says it is in use.

### In the popup

| Key | Does |
|---|---|
| ↓ / ↑ | Move between rows (section headers are skipped) |
| Home / End | First / last row |
| Space | Expand or collapse the row (or the section) |
| Enter or O | Open the item in the browser |
| A | Approve |
| R | Reject |
| M | Merge |
| C | Re-run failed |
| S | Open the row's snooze menu |
| Enter | Confirm, once the 600 ms guard has passed |
| Esc | Cancel the confirm; otherwise hide the popup |
| / or Ctrl+F | Search |

An action key does nothing on a row that doesn't offer that action. Letters typed in the search or comment box are text, never
commands. Opening the popup selects the first row, and the selection stays on the same item across refreshes.

## Search

Press `/` or Ctrl+F, or click 🔍 in the header. Type words to filter rows; every word must match the repo, the title, the
author, or the PR number (`#12` or `12`). Matching is case-insensitive. Sections with no match disappear, Waiting and Snoozed
open while you search, and counts show the filtered numbers. The tray icon and badge are not filtered.
**Esc** clears the search (a second **Esc** hides the popup), and hiding the popup clears it too.
