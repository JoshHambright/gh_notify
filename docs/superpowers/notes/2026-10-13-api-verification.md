# API / CLI verification notes (Task 0 spike)

Date: 2026-10-13. Read-only; no tokens or content recorded. Windows, Git Bash, no jq/node/python on PATH (use PowerShell `ConvertFrom-Json` or `gh --jq` for ad-hoc parsing).

## Step 1: GitHub notifications

- **Confirmed**: `gh api -X GET notifications -f all=true -f per_page=50` works. Reasons seen: `author`, `comment`, `review_requested`, `state_change`; subject types `PullRequest`, `Issue`.
- **Unverified**: no `approval_requested` entries in the last 50, so its `subject.type` is unknown (reason "no recent deployment reviews"). Not blocking; Task 6 also scans repos from the user's PRs.

## Step 2: GraphQL inbox query

- **Confirmed**: query runs with no `errors`, with the exact `-F reviewQ/-F mineQ/-f query` invocation. `viewer.login`, `review.nodes[]`, `mine.nodes[]` shapes match. `mine` nodes carry `reviewDecision` (observed `REVIEW_REQUIRED`), `commits.nodes[0].commit.statusCheckRollup.state` (observed `SUCCESS`), `reviewThreads.nodes[].{isResolved, comments.nodes[0].author.login}`.
- **Differs / notes**:
  - `statusCheckRollup` can be `null` (1 of 3 PRs) - must be nullable.
  - Bot author in GraphQL: `author { __typename: "Bot", login: "dependabot" }` - login has NO `[bot]` suffix (REST uses `dependabot[bot]`). Humans: `__typename: "User"`. `author` can be null for deleted users (handle null).
  - `review` list was empty here (no review requests currently), so its shape is only verified via the shared `pr` fragment on `mine`.

## Step 3: Pending deployments

- **Confirmed**: `GET repos/<o>/<r>/actions/runs?status=waiting` returns `total_count` and `workflow_runs[]` with `id, name, display_title, html_url, head_sha, updated_at, actor.login`. A real waiting run existed.
- **Confirmed**: `GET .../actions/runs/<id>/pending_deployments` returns an array of objects with keys `environment{id,node_id,name,url,html_url}`, `wait_timer`, `wait_timer_started_at`, `current_user_can_approve` (bool), `reviewers[]`. `environment.name` and `current_user_can_approve` both present.
- Note: `GET repos/<o>/<r>/environments` lists environment names (useful to decide which repos to scan).

## Step 4: Azure DevOps

- **Unverified - az not usable for ADO.** `az` has accounts but `az account get-access-token --resource 499b84ac-...` fails with AADSTS500011 (default tenant has no ADO resource principal); with the other tenant it fails AADSTS50020 (account not in that tenant). Needs interactive `az login --tenant <ado tenant> --scope 499b84ac-1321-427f-aa17-267ca6b56798/.default`, which was not attempted per rules. All ADO endpoint shapes (profile, accounts, connectionData, identities memberOf, descriptors, projects, pullrequests, policy evaluations, pipeline approvals, expiry fields `expires_on`/`expiresOn`) remain unverified. Note: the token acquisition failure itself is a real-world mode Task 7 must surface as an auth error ("needs az login"), not crash.

## Step 5: Claude CLI (v2.1.296)

- **Confirmed** flags exist: `-p/--print`, `--output-format`, `--permission-mode`, `--allowedTools`, `--disallowedTools`, `-r/--resume [value]`.
- **Confirmed** deny rule: with `--allowedTools "Bash(gh:*)" --disallowedTools "Bash(gh pr comment:*)"`, `gh pr comment 1 --body test` was denied (run in an empty non-git dir). JSON output includes `permission_denials: [{tool_name, tool_use_id, tool_input:{command,...}}]`, an authoritative signal of denial.
- **JSON fields present** (single object, `type: "result"`): `session_id`, `result` (string), `is_error` (bool), `total_cost_usd`, `subtype` ("success"), `num_turns`, `duration_ms`, `stop_reason`, `terminal_reason`, `usage`, `modelUsage`, `permission_denials`, `api_error_status`.
- **Confirmed** resume: `echo ... | claude -p --output-format json --resume <session_id>` from the same directory restored context (answered with the earlier command) and returned the same `session_id`. (Interactive `claude --resume <id>` in the same cwd is the same mechanism; not launched interactively.)
- **Middle wildcards**: `Bash(gh api * -f *)` was accepted as an allow rule and matched `gh api repos/x/y/issues -f a=b --method GET`. Middle-wildcard deny `Bash(gh pr * --body *)` also worked (denied `gh pr comment 1 --body test`).
- Environment caveat: no `python`/`jq`/`node` on PATH.

## Corrections for later tasks

- Task 5: `statusCheckRollup` is nullable - model it as `string?`/null-safe. `author` may be null; handle it.
- Task 5: GraphQL Bot logins have no `[bot]` suffix (`dependabot`, `__typename: "Bot"`); detect bots via `__typename == "Bot"`, not by login suffix. REST (notifications/actions actor) would use `dependabot[bot]`.
- Task 6: no `approval_requested` notification observed; keep the repo-scan path as the primary source for deployment reviews. Pending deployments confirmed: use `environment.name` and `current_user_can_approve` from `pending_deployments` (plus `reviewers[]`, `wait_timer`).
- Task 7-9: ADO shapes are UNVERIFIED; implement defensively (tolerate `memberOf` as strings or objects, both `expires_on` and `expiresOn`) and treat AADSTS500011/50020 token errors as "not signed in to ADO tenant" in the auth layer; the correct tenant login must be done before ADO can be tested. Re-run Step 4 after `az login --tenant <ado tenant> --scope 499b84ac-1321-427f-aa17-267ca6b56798/.default`.
- Task 12: parse the single JSON result object; check `permission_denials` (array) and `is_error`; `session_id` is stable across `--resume`. Middle wildcards (`Bash(gh api * -f *)`) are valid in allow and deny rules in 2.1.296. Use `--resume <id>` with `-p` for non-interactive continuation.
