# Workflows: agent profiles, rules and hooks

A **profile** is something gh_notify can run for an item: either a Claude prompt or a command. A **rule** says which profile
runs for which item, and whether it runs by itself. Edit all of this in **Settings › Workflows**, or in `config.json`
(see [configuration.md](configuration.md)).

## Built-in profiles

None of these needs a skill, except the last two, which call a skill you install yourself.

| Profile | Button | Runs by default for | What it does |
|---|---|---|---|
| `review` | Pre-review | `ReviewRequested` | Reads the diff and surrounding code and lists bugs, risky changes and missing tests, most important first, with `file:line`. Posts nothing. |
| `diagnose-ci` | Diagnose | `MyPrCiFailing`, `MyPrNeedsResponse` | Reads the failing check logs and code, finds the root cause of each failure and suggests the smallest fix. Makes no commits, pushes or comments. |
| `peer-review` | Pre-review | nobody | Runs your `/peer-review` skill unattended, up to the findings report, and posts nothing. Needs the skill. |
| `pr-shepherd` | Diagnose | nobody | Runs your `/pr-shepherd` skill in diagnose-only mode: no commits, pushes or thread replies. Needs the skill. |

Approvals, ready-to-merge and waiting items have no default profile. Custom profiles get a button labelled with their name.
Built-ins are read-only in Settings; use **Duplicate to edit**, or define a profile with the same name in `agentProfiles`.

The exact built-in prompts:

- `review`: *Review the pull request at {url}. Read the diff and the surrounding code. List bugs, risky changes and missing tests, most important first, with file:line. Do not post, approve, comment or push anything. End with a 1–3 line summary starting "SUMMARY:".*
- `diagnose-ci`: *The pull request at {url} has failing checks. Read the failing check logs and the relevant code, find the root cause of each failure, and suggest the smallest fix. Make no commits, pushes or comments. End with a 1–3 line summary starting "SUMMARY:".*
- `peer-review`: */peer-review {url} — run unattended: complete the review through writing the findings report, then STOP. Do not post anything. End with a 1–3 line summary starting "SUMMARY:".*
- `pr-shepherd`: */pr-shepherd {url} — diagnose only: find the real reason for each failure and triage comments. Make no commits, pushes, or thread replies. End with a 1–3 line summary starting "SUMMARY:".*

A skill counts as installed if its `SKILL.md` is in `~/.claude/skills/<name>/` or in an installed Claude Code plugin. Skills in a
repo's own `.claude/skills/` don't count, because agents ignore the PR's own settings (see [security.md](security.md)). If a
required skill is missing, the button is disabled and its tooltip says why.

## How rules pick a profile

For each item, gh_notify walks `rules` in order and takes the **first** rule whose `match` fits (all given keys must match; see
[Item match](configuration.md#item-match)). No match means mode `button` and the default profile for the item's kind.

- `agent`: `auto` runs the profile as soon as the item appears; `button` shows a button and waits for your click; `none` (Off)
  turns the profile off for matching items.
- `profile`: the profile to run. Left out, it falls back to the default for the kind.

The default for a kind comes from `defaultProfiles`, then the built-ins above. Muted and snoozed items never auto-run (their
button still works). Concurrency is capped by `maxConcurrentAgents`, runs time out after `agentTimeoutMinutes`.

```json
"defaultProfiles": { "ReviewRequested": "peer-review", "MyPrCiFailing": "pr-shepherd", "MyPrNeedsResponse": "pr-shepherd" },
"rules": [
  { "match": { "author": "dependabot[bot]" }, "agent": "none" },
  { "match": { "repo": "acme/core-*", "kind": "ReviewRequested", "maxChangedLines": 400 }, "agent": "auto" }
]
```

The first rule turns agents off for bot PRs; the second pre-reviews small PRs in `acme/core-*` automatically.

## Writing a Claude prompt profile

```json
"agentProfiles": {
  "deploy-check": {
    "prompt": "Check the deployment behind {url} in {repo}. Report only. End with \"SUMMARY:\".",
    "needsWorktree": false
  }
}
```

The prompt goes to `claude` on stdin and may use these placeholders, filled once (a value that itself contains `{url}` is
not substituted again):

| Placeholder | Value |
|---|---|
| `{url}` | The item's web URL. |
| `{repo}` | `org/repo` (ADO: `org/project/repo`). |
| `{number}` | PR number, or empty. |
| `{title}` | PR or approval title. |
| `{kind}` | The item kind, such as `ReviewRequested`. |
| `{provider}` | `GitHub` or `Ado`. |
| `{author}` | Author's login. |
| `{detail}` | The row's detail line (check state, environment, and so on). |

Tips: end with "SUMMARY:" and one to three lines; the first line starting with it becomes the row's summary. Set
`requiresSkill` if the prompt invokes a skill. `allowedTools` and `disallowedTools` add to the global lists; the always-on deny
list can't be removed.

## Writing a command profile

A command profile runs any program instead of Claude. Exactly one of `prompt` or `command` per profile.

```json
"agentProfiles": {
  "triage": {
    "command": ["pwsh", "-NoProfile", "-File", "C:\\scripts\\triage.ps1", "{url}"],
    "needsWorktree": false
  }
}
```

`triage.ps1`:

```powershell
param([string]$Url)
$ErrorActionPreference = 'Stop'

Write-Host "Triaging $env:GHN_REPO #$env:GHN_NUMBER ($env:GHN_KIND) by $env:GHN_AUTHOR"
$files = gh pr diff $Url --name-only
Write-Host "$($files.Count) files changed"

if ($files -match 'migrations/') { Write-Host 'SUMMARY: touches database migrations, review carefully' }
else { Write-Host "SUMMARY: $($files.Count) files, no migrations" }
exit 0
```

**Placeholders.** Every argument after the program is filled with the same `{url}`, `{repo}`, `{number}`, `{title}`,
`{kind}`, `{provider}`, `{author}`, `{detail}` placeholders, one argument at a time. A PR title stays a single argument; it is
never split or interpreted by a shell.

**Environment variables** set for the process:

| Variable | Value |
|---|---|
| `GHN_URL` | Item URL. |
| `GHN_REPO` | Repo (`org/repo`, ADO `org/project/repo`). |
| `GHN_NUMBER` | PR number, or empty. |
| `GHN_TITLE` | Title. |
| `GHN_KIND` | Item kind. |
| `GHN_PROVIDER` | `GitHub` or `Ado`. |
| `GHN_AUTHOR` | Author. |
| `GHN_ITEM_KEY` | gh_notify's stable key for the item. |

**Result.**

- Output is stdout and stderr together; only the **last 64 KB** is kept.
- The summary is the first line starting `SUMMARY:` (case-insensitive), else the last non-empty line, cut at 200 characters.
- Exit code `0` means **Ready**. Any other code means **Failed** with "exit N". A run over `agentTimeoutMinutes` fails as "timed out".
- A program that isn't on `PATH` fails with "'<name>' not found" (and the button is disabled).
- No cost is shown, and **Open in Claude** is hidden (there is no session). `allowedTools` / `disallowedTools` don't apply.

**Working directory.** With `needsWorktree: true`, a throwaway worktree of the PR's local clone (found under `repoRoots`). With
`false`, the local clone if one is found, otherwise `%TEMP%\GhNotify\cmd`.

**`.cmd` / `.bat` caveat.** Batch files run through `cmd.exe`, which can't quote safely. If any argument, after placeholder
filling, contains `"`, `%`, `!`, CR or LF, the run is refused. A PR title with a `%` will therefore fail for a `.cmd` target. Use
a `.ps1` (via `pwsh -File`) or an `.exe` for arguments built from titles, or read `GHN_TITLE` from the environment instead.

## Hooks

Hooks run a program when something happens to an item:

```json
"hooks": [
  { "event": "ItemAdded", "match": { "kind": "ApprovalPending" }, "run": ["pwsh", "-File", "C:\\scripts\\ping-team.ps1"] },
  { "event": "AgentFinished", "run": ["C:\\scripts\\log-run.cmd"] }
]
```

| Key | Meaning |
|---|---|
| `event` | `ItemAdded`, `ItemRemoved`, `ItemChanged` (new commit, vote or check state) or `AgentFinished`. |
| `match` | Optional [item match](configuration.md#item-match). |
| `run` | Program and arguments. Arguments are literal; no placeholders. |

Each hook receives the item as JSON on stdin (`{ "event", "item", "agent" }`) and the environment variables `GHN_EVENT`,
`GHN_KEY`, `GHN_URL`, `GHN_KIND` and `GHN_REPO`. A hook is stopped after 60 seconds, and a failing hook is logged, never shown as an error.
