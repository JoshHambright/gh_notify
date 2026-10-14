# Notifications: work hours, sounds, mute, hide and snooze

## Work hours

gh_notify only toasts during work hours: `workHours.days` between `workHours.start` and `workHours.end`, local time
(default Mon–Fri, 08:00–18:00). Outside them the inbox still updates and the tray icon still counts, but nothing toasts or
plays a sound, except for items whose sound rule sets `alertOffHours` (below). Set the hours in **Settings › General**.

## Sound rules

`notifications.sounds` is a list of rules. The **first** rule whose `match` fits the item decides its sound. With no matching
rule the toast uses the Windows default sound. Rules use the same [item match](configuration.md#item-match) as workflow rules.

```json
"notifications": {
  "sounds": [
    { "match": { "kind": "ApprovalPending", "repo": "acme/*" }, "sound": "builtin:alarm", "alertOffHours": true },
    { "match": { "kind": "ReviewRequested" }, "sound": "C:\\sounds\\ding.wav" },
    { "match": { "author": "dependabot[bot]" }, "sound": "none" },
    { "match": { "kind": "MyPrReadyToMerge" }, "sound": "builtin:chime" }
  ]
}
```

| `sound` value | Effect |
|---|---|
| `default` | The toast's own Windows sound. |
| `none` | A silent toast. |
| `builtin:<name>` | One of the built-in tones below. |
| an absolute path | A `.wav` file you provide. A missing or unreadable file falls back to the default sound and logs a warning once. |

**Built-in sounds:** `chime`, `ping`, `alert`, `alarm`, `soft`. They are original tones that ship with the app. In
**Settings › Notifications**, the ▶ button plays the selected sound.

### One sound per poll

If one poll adds several items, only **one** sound plays; every item still gets its own (silent) toast. The sound that plays is
the one for the most important new item, in this order:

1. `ApprovalPending`
2. `MyPrCiFailing`
3. `ReviewRequested`
4. `MyPrNeedsResponse`
5. `MyPrReadyToMerge`

Ties go to the oldest item. `MyPrWaiting` items never toast and never play a sound.

### Alert off-hours

A sound rule with `"alertOffHours": true` lets matching items toast and play their sound even outside work hours. Use it for
the things you'd genuinely want to be woken up for, such as production approvals.

Muted items never toast or play, whatever the sound rules say.

## Mute, hide and snooze

| | Applies to | Still visible? | Toast / sound / auto-agent | Counted in badge | Comes back |
|---|---|---|---|---|---|
| **Snooze** | One item | Only in the "Snoozed (n)" section | No | No | By itself: at the time, when the item changes, or when you unsnooze |
| **Mute** | A repo, org or project | Yes, dimmed, tagged 🔇 ("Muted by <pattern>") | No (the Run button still works) | No | Timed mutes expire; forever mutes stay until you remove the pattern |
| **Hide** | A repo, org or project | No | No | No | When you remove the pattern |

Hiding is purely a display filter; gh_notify still tracks the items, so unhiding brings them back with no stale or duplicate
toasts.

### Pattern syntax

`filters.mute` and `filters.hide` take patterns:

- An optional provider prefix, `gh:` or `ado:`, then `/`-separated segments.
- A GitHub item's path is `org/repo`. An Azure DevOps item's is `org/project/repo` (pipeline approvals: `org/project/<pipeline>`).
- A pattern matches the **first N segments** of the path, N being the pattern's segment count. So an org name covers the whole org,
  and `org/project` covers a whole project.
- `*` matches within one segment. Matching ignores case.
- Empty segments and `**` are invalid; the config won't save.

| Pattern | Matches |
|---|---|
| `contoso` | Everything in the `contoso` org, on either provider. |
| `contoso/platform` | The `platform` repo in GitHub org `contoso`, or the whole `platform` project in ADO org `contoso`. |
| `gh:acme/legacy-*` | GitHub repos in `acme` whose name starts with `legacy-`. |
| `ado:contoso/platform` | Only the ADO project `contoso/platform`. |
| `gh:fabrikam/sandbox` | Only that GitHub repo. |

Manage the lists in **Settings › Filters**, which also shows how many current items each pattern matches.

### Timed mutes

Each row's **⋯** menu has **Mute repo**, **Mute org** and, for Azure DevOps, **Mute project**, each for **1 hour**, **Until
tomorrow** (the next work-day start) or **Forever**. Timed mutes are kept in the state file and expire on their own. **Forever**
adds the pattern to `filters.mute`, exactly as if you had typed it in Settings. The popup's **⋯** menu lists active timed mutes
with their time left and an **Unmute** button.

### Snooze

The row's **⋯** menu also has **Snooze**: **1 hour**, **4 hours**, **Until tomorrow**, **Next week** (Monday's work-day start),
or **Until it changes**. A snoozed item leaves its section and the counts, doesn't toast or auto-run, and its toast is removed
from Action Center.

A snooze ends when its time passes, when you click **Unsnooze**, or when the item **changes** (a new commit, a new unresolved
thread, a changed approval or vote state). It then returns to its section and toasts like a new item, with the title prefix
"Back: ". If the item leaves the inbox meanwhile (merged, approved, closed), the snooze is dropped without a toast. Snoozes
survive restarts.

## The Waiting and Snoozed sections

- **My PRs — ready to merge**: your approved, green, mergeable PRs. They count in the badge, toast as "Ready to merge" and are
  merged in the browser.
- **Waiting on others (n)**: every other open PR of yours ("Waiting on review", "Checks running", "Waiting on N reviewers"). It
  is collapsed by default, remembers whether you expanded it, never toasts or sounds, and isn't counted in the badge. Filters
  still apply.
- **Snoozed (n)**: your snoozed items, collapsed at the bottom, each with time left (or "until it changes") and **Unsnooze**.
