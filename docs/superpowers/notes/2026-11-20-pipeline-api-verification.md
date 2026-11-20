# Pipeline API verification (2026-11-20)

Read-only probes with `gh` and the `az` token, against this repo's Actions and one real ADO org. Field names below were seen in real responses.

## ADO Pipelines

- Projects: `GET https://dev.azure.com/{org}/_apis/projects?api-version=7.1` (already used by `AdoDirectory`).
- Definitions (picker): `GET {org}/{project}/_apis/build/definitions?api-version=7.1` → `value[].{id,name,path}`.
- Builds: `GET {org}/{project}/_apis/build/builds?definitions={id}&queryOrder=finishTimeDescending&minTime={iso}&$top=N&api-version=7.1`. Encode `$top` as `%24top`. Without `statusFilter` in-progress builds are returned (`status` `inProgress`/`notStarted`, no `result`). Fields used: `id`, `buildNumber`, `status`, `result` (`succeeded|partiallySucceeded|failed|canceled`), `sourceBranch` (`refs/heads/main`), `sourceVersion`, `finishTime`, `queueTime`, `requestedFor.{id,displayName,uniqueName}`, `_links.web.href`, `definition.{id,name}`.
- Timeline: `GET {org}/{project}/_apis/build/builds/{id}/timeline?api-version=7.1` → `records[]` with `type` in `Stage|Phase|Job|Task|Checkpoint`. `Stage` records have `name`, `state` (`pending|inProgress|completed`), `result` (`succeeded|succeededWithIssues|failed|canceled|skipped`), `attempt`, `finishTime`.
- Decisions: stage = timeline `Stage.name` only (no separate environment match in v1). `succeededWithIssues` maps to PartiallySucceeded. `canceled`/`skipped`/non-completed map to no outcome. The `minTime` filter is applied to the build's `queueTime`-ordered/finish window by the service, so the feed queries one day before `createdAt` and filters on `finishTime` itself.

## GitHub Actions

- Workflows (picker): `GET /repos/{r}/actions/workflows` → `workflows[].{id,name,path}`.
- Runs: `GET /repos/{r}/actions/workflows/{id}/runs?created=>={iso}&per_page=N`. Fields: `id`, `run_number`, `run_attempt`, `status`, `conclusion`, `head_branch`, `head_sha`, `html_url`, `updated_at`, `run_started_at`, `triggering_actor.login`, `actor.login`.
- Jobs: `GET /repos/{r}/actions/runs/{id}/jobs` (use `?filter=latest`). **A job has no `environment` field** (keys seen: `name,status,conclusion,started_at,completed_at,run_attempt,...`). `/repos/{r}/deployments` returned nothing for a repo without deployment events and does not link to runs reliably.
- Decision: GitHub "stage" = **job name**. The Settings label for GitHub is "Job (e.g. deploy-prod*)". Environment matching is deferred. Run finish time = `updated_at` for a completed run; job finish time = `completed_at`.
- No partially-succeeded concept: `conclusion` `success→Succeeded`, `failure|timed_out→Failed`, `cancelled|skipped|neutral→none`.

## Scopes

No new token scopes were needed for these calls (the ADO token already reads builds and timelines; `gh` reads public Actions data and, for private repos, the existing `repo` scope used for approvals).
