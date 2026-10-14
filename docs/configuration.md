# Configuration

gh_notify reads `%APPDATA%\GhNotify\config.json`. It is created on first run and reloaded when you save it.
**Settings** (tray menu or popup **⋯**) edits it for you; **Settings › Advanced** opens the file.

Saving from Settings rewrites the file, so JSON comments are dropped. A backup of the previous file is kept as
`config.json.bak`. Unknown keys are preserved. If the file is invalid, the popup shows a banner with **Fix in Settings**.
Property names are camelCase and enums are strings. Most changes apply on save; a change to `maxConcurrentAgents`
needs a restart (quit from the tray and start again).

For tests and development, `GHNOTIFY_CONFIG_DIR` and `GHNOTIFY_DATA_DIR` override the config and data folders.

## Top level

| Key | Type | Default | Meaning |
|---|---|---|---|
| `pollSeconds` | int | `120` | Seconds between polls. At least 30. |
| `workHours` | object | see below | When toasts are allowed. |
| `github` | object | see below | GitHub source. |
| `ado` | object | see below | Azure DevOps source. |
| `repoRoots` | string[] | `["C:\\dev"]` | Folders searched for local clones (agents run in a worktree of the clone). |
| `maxConcurrentAgents` | int | `2` | Agent runs at once. At least 1. Needs a restart. |
| `agentTimeoutMinutes` | int | `20` | Kill an agent or command run after this long. At least 1. |
| `agentAllowedTools` | string[] | read-only set: `Read`, `Grep`, `Glob`, `gh pr view/diff/checks`, `gh run view`, `gh api`, `git`, read-only `az repos pr` / `az pipelines` verbs, `bash` | Tools Claude agents may use, on top of the profile's own. The always-on deny list is separate; see [security.md](security.md). |
| `display` | object | see below | Popup display. |
| `updates` | object | see below | Auto-update. |
| `notifications` | object | `{ "sounds": [] }` | Sound rules. |
| `filters` | object | `{ "mute": [], "hide": [] }` | Mute and hide patterns. |
| `rules` | object[] | `[]` | Which profile runs for which item. |
| `defaultProfiles` | object | `{}` | Item kind → profile, overriding the built-in defaults. |
| `agentProfiles` | object | `{}` | Your own profiles, by name. |
| `hooks` | object[] | `[]` | Commands run on item events. |

## `workHours`

| Key | Type | Default | Meaning |
|---|---|---|---|
| `days` | string[] | `["Mon","Tue","Wed","Thu","Fri"]` | Days toasts are allowed (`Mon`…`Sun`). |
| `start` | string | `"08:00"` | Start, `HH:mm`, local time. |
| `end` | string | `"18:00"` | End, `HH:mm`. |

## `github`

| Key | Type | Default | Meaning |
|---|---|---|---|
| `enabled` | bool | `true` | Watch GitHub. |
| `includeDrafts` | bool | `false` | Include your draft PRs. |
| `watchRepos` | string[] | `[]` | Extra `owner/repo` names to watch. |

## `ado`

| Key | Type | Default | Meaning |
|---|---|---|---|
| `enabled` | bool | `true` | Watch Azure DevOps. |
| `orgs` | string[] | `[]` | Orgs to watch, in addition to discovered ones. |
| `discoverOrgs` | bool | `true` | Find your orgs automatically. |
| `excludeOrgs` | string[] | `[]` | Orgs to skip (unchecking an org in Settings adds it here). |
| `includeDrafts` | bool | `false` | Include your draft PRs. |
| `azSubscription` | string? | `null` | Request the `az` token with `--subscription`. Preferred when your default `az` account isn't the Azure DevOps one. |
| `azTenant` | string? | `null` | Request the token with `--tenant` instead. |

## `display`

| Key | Type | Default | Meaning |
|---|---|---|---|
| `repoAliases` | object | `{}` | Prefix → short name, applied to repo names in the popup. |
| `maxRepoChars` | int | `34` | Longer repo names are shortened in the middle, keeping the repo segment. At least 12. |

`display.repoAliases` shortens repo names in the popup. A key is an org (`"contoso-engineering"`) or an org/project prefix
(`"contoso-engineering/platform"`). The longest matching prefix is replaced by the value. A name still longer than
`display.maxRepoChars` is shortened in the middle, for example `contoso-engi…/checkout-service-api`. The full name is in the
tooltip and in the expanded details.

## `updates`

| Key | Type | Default | Meaning |
|---|---|---|---|
| `enabled` | bool | `true` | Check for and download updates (installed builds only). |
| `checkHours` | int | `4` | Hours between checks. 1–168. |

## `notifications.sounds[]`

First matching rule wins. See [notifications.md](notifications.md).

| Key | Type | Default | Meaning |
|---|---|---|---|
| `match` | object | (matches all) | An [item match](#item-match). |
| `sound` | string | `"default"` | `default`, `none`, `builtin:<name>` (`chime`, `ping`, `alert`, `alarm`, `soft`), or an absolute path to a `.wav`. |
| `alertOffHours` | bool | `false` | Toast and play even outside work hours. |

## `filters`

| Key | Type | Default | Meaning |
|---|---|---|---|
| `mute` | string[] | `[]` | Patterns: still listed (dimmed), but no toast, sound or auto-agent, and not counted. |
| `hide` | string[] | `[]` | Patterns: removed from the popup, badge and toasts. |

Pattern syntax is in [notifications.md](notifications.md#pattern-syntax). Timed mutes and snoozes live in the state file,
not here.

## `rules[]`

First matching rule wins. See [workflows.md](workflows.md#how-rules-pick-a-profile).

| Key | Type | Default | Meaning |
|---|---|---|---|
| `match` | object | (matches all) | An [item match](#item-match). |
| `agent` | string | `"button"` | `auto` (run when the item appears), `button` (run on click), `none` (off). |
| `profile` | string? | per-kind default | Profile name, built-in or yours. Must exist. |

## `defaultProfiles`

Maps an item kind to a profile name. Kinds: `ReviewRequested`, `ApprovalPending`, `MyPrCiFailing`, `MyPrNeedsResponse`,
`MyPrReadyToMerge`, `MyPrWaiting`. Built-in defaults when a kind isn't listed: `ReviewRequested` → `review`;
`MyPrCiFailing` and `MyPrNeedsResponse` → `diagnose-ci`; the rest have none.

## `agentProfiles.<name>`

Each profile has **exactly one** of `prompt` or `command`. Built-in names (`review`, `diagnose-ci`, `peer-review`,
`pr-shepherd`) can be overridden by defining the same name.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `prompt` | string | `""` | A Claude prompt. Placeholders: see [workflows.md](workflows.md#writing-a-claude-prompt-profile). |
| `command` | string[] | none | A program and its arguments, run instead of Claude. See [workflows.md](workflows.md#writing-a-command-profile). |
| `needsWorktree` | bool | `true` | Run in a throwaway git worktree of the PR. |
| `requiresSkill` | string? | `null` | Skill the prompt invokes. If it isn't installed, the run button is disabled. |
| `allowedTools` | string[] | `[]` | Extra tools for this profile (Claude only). |
| `disallowedTools` | string[] | `[]` | Extra denied tools (Claude only). |

## `hooks[]`

| Key | Type | Default | Meaning |
|---|---|---|---|
| `event` | string | `ItemAdded` | `ItemAdded`, `ItemRemoved`, `ItemChanged` or `AgentFinished`. |
| `match` | object | (matches all) | An [item match](#item-match). |
| `run` | string[] | `[]` | Program and arguments. Must name a program. |

## Item match

Used by `rules`, `notifications.sounds` and `hooks`. Every key you give must match; omitted keys match anything.

| Key | Type | Meaning |
|---|---|---|
| `provider` | string | `GitHub` or `Ado`. |
| `repo` | string | Glob against the repo (`org/repo`, ADO `org/project/repo`); `*` and `?` work. Case-insensitive. |
| `kind` | string | An item kind (see `defaultProfiles`). |
| `author` | string | Exact author, case-insensitive (for example `dependabot[bot]`). |
| `maxChangedLines` | int | Only PRs with at most this many changed lines. |

## Example

```jsonc
{
  "pollSeconds": 120,
  "workHours": { "days": ["Mon","Tue","Wed","Thu","Fri"], "start": "08:00", "end": "18:00" },
  "github": { "enabled": true, "includeDrafts": false, "watchRepos": ["acme/deploy"] },
  "ado": {
    "enabled": true,
    "orgs": ["contoso"],
    "discoverOrgs": true,
    "excludeOrgs": ["fabrikam-sandbox"],
    "includeDrafts": false,
    "azSubscription": "contoso-engineering"
  },
  "repoRoots": ["C:\\dev", "D:\\work"],
  "maxConcurrentAgents": 2,
  "agentTimeoutMinutes": 20,
  "display": { "repoAliases": { "contoso-engineering": "contoso" }, "maxRepoChars": 34 },
  "updates": { "enabled": true, "checkHours": 4 },
  "notifications": {
    "sounds": [
      { "match": { "kind": "ApprovalPending", "repo": "acme/*" }, "sound": "builtin:alarm", "alertOffHours": true },
      { "match": { "kind": "ReviewRequested" }, "sound": "C:\\sounds\\ding.wav" },
      { "match": { "author": "dependabot[bot]" }, "sound": "none" }
    ]
  },
  "filters": {
    "mute": ["acme/legacy-*", "ado:contoso/platform"],
    "hide": ["gh:fabrikam/sandbox"]
  },
  "defaultProfiles": { "ReviewRequested": "review", "MyPrCiFailing": "diagnose-ci" },
  "rules": [
    { "match": { "author": "dependabot[bot]" }, "agent": "none" },
    { "match": { "repo": "acme/core-*", "kind": "ReviewRequested", "maxChangedLines": 400 }, "agent": "auto" },
    { "match": { "kind": "MyPrCiFailing" }, "agent": "auto" }
  ],
  "agentProfiles": {
    "triage": {
      "command": ["pwsh", "-NoProfile", "-File", "C:\\scripts\\triage.ps1", "{url}"],
      "needsWorktree": false
    },
    "deploy-check": {
      "prompt": "/post-deploy-verify {url} — report only. End with \"SUMMARY:\".",
      "needsWorktree": false,
      "requiresSkill": "post-deploy-verify"
    }
  },
  "hooks": [
    { "event": "ItemAdded", "match": { "kind": "ApprovalPending" }, "run": ["pwsh", "-File", "C:\\scripts\\ping-team.ps1"] }
  ]
}
```
