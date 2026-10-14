# Security

## Tokens

gh_notify never stores a credential. It asks the tools you already use, each time it needs one:

- GitHub: `gh auth token`
- Azure DevOps: `az account get-access-token` (with `ado.azSubscription` / `ado.azTenant` if set)

Tokens are held in memory for the request and go only to GitHub's and Azure DevOps's own HTTPS APIs. They are not written to
the config, the state file or the logs.

## Agents

Agents are for reading. Every Claude run gets an always-on deny list on top of the prompt's instructions, and the list can't be
loosened from the config. Summarised by category, agents **cannot**:

- comment on, review, approve, merge, close, edit or mark-ready a pull request (`gh pr review|comment|merge|close|edit|ready`, `gh issue comment`)
- write through the API: any `gh api` call with a non-GET method, a field or an input file
- push or commit (`git push`, `git commit`, including with global options such as `git -C . push`)
- create things (`gh pr|issue|release create`, `az repos pr create|update|set-vote|reviewer|work-item`, `az repos create|update|delete|import|ref|policy`)
- run or change pipelines (`az pipelines run`, `build queue`, `release create`, `variable`, `variable-group`, `create`, `update`, `delete`)
- call `az rest` or `az devops invoke`

The default allow list (`agentAllowedTools`) is read-only: file reads and search, `gh pr view/diff/checks`, `gh run view`,
read-only `gh api`, `git`, and read-only `az repos pr` / `az pipelines` verbs. Treat the deny list as a best-effort second line
of defence behind the prompt, not a sandbox.

Other isolation:

- **`--setting-sources user`.** The checkout is the PR's own, untrusted code. Runs only load your user settings, so the PR's
  `.claude/settings.json` (hooks, permission rules) never applies. For the same reason a repo's own `.claude/skills/` aren't
  loaded; keep skills in `~/.claude/skills/` or a plugin.
- **Throwaway worktrees.** Runs happen in a separate git worktree of your local clone, removed afterwards (and swept on startup).
  Your working tree is never touched.
- **Timeouts and caps.** Runs stop after `agentTimeoutMinutes`, at most `maxConcurrentAgents` at a time.

**Open in Claude** resumes the exact session in Windows Terminal, so anything that would be posted still goes through you.

## Command profiles

A `command` profile runs whatever you configure, with your user's permissions, and none of the deny list above applies. Arguments
are passed as separate argv entries (never through a shell; `.cmd` / `.bat` targets reject unsafe characters), but the program
itself is trusted. Only add commands you would run yourself. Hooks are the same.

## In-app approvals

Approving or rejecting from the tray is deliberately slow:

1. **Two steps.** The first click only checks and shows what will happen ("Approve deployment of acme/deploy run 9001 to
   production"). A second click on a button naming the target sends it.
2. **Live re-check.** The state is fetched again just before sending. If it changed (new commits, already handled), the action
   is refused and you're asked to refresh.
3. **600 ms and pointer guard.** The confirm button is disabled for 600 ms after it appears and whenever the popup is re-shown,
   and a mouse click only counts after the pointer has entered the enabled button and then moved, so a double-click can't go through both steps.
4. **Stale sources disable confirm.** If a source is failing or its data is stale, confirm stays disabled.

Actions use your own `gh` / `az` login. Agents can't trigger them.

## Updates

The app checks GitHub Releases for this repository over HTTPS, every `updates.checkHours` hours (turn off with
`updates.enabled`). Updates are downloaded quietly and applied when you choose **Restart to update**, or otherwise the next time gh_notify starts; the app never restarts on its own.
Builds are **unsigned for now**, hence the SmartScreen prompt. The signing step is stubbed in the release workflow for later.
`GHNOTIFY_UPDATE_FEED` points the updater at a local folder; it exists for the install tests and shouldn't be set otherwise.

## Leak scan in CI

CI greps the whole tree and every commit message for a private list of terms (real org, client and tenant names), kept in a
repository secret, never in the repo. A hit fails the build and prints only `file:line`. All names in code, docs, tests and
screenshots are placeholders (contoso, fabrikam, acme).
