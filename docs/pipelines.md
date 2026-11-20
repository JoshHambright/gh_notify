# Pipelines: alert when a run ends the way you care about

Follow an Azure DevOps pipeline or a GitHub Actions workflow and get a popup when a run ends in an outcome you chose, for example
"the deploy pipeline failed on PROD".

## Follow a pipeline

Settings › **Pipelines** › **Add pipeline…**

1. Pick the provider. For Azure DevOps choose the organization, the project and the pipeline. For GitHub enter `owner/repo`,
   choose **Find workflows**, then pick the workflow.
2. Choose **Alert when**: Failed, Succeeded, and (Azure DevOps only) Succeeded with issues.
3. Optionally narrow it:
   - **Stages** (Azure DevOps) or **Jobs** (GitHub): globs such as `PROD*`. With stages set, the alert fires as soon as that
     stage finishes, even while the rest of the run is still going.
   - **Branches**: globs such as `main, release/*`.
   - **Only runs I triggered**.

Only runs that finish **after** you add a pipeline alert. Editing a subscription starts it afresh, so older runs are never replayed.

## What you see

Matching runs appear in the popup's **Pipelines** section and toast once ("Pipeline failed", "Pipeline succeeded",
"Pipeline succeeded with issues"). A row stays until you dismiss it: **Dismiss** on the row, **D** on the keyboard, or
**Dismiss all** on the section header. Only **failed** rows count toward the tray badge. Rows are mute- and hide-able like any
other, and sound rules can target them with `"kind": "PipelineRun"`.

Pipeline rows have no agent actions, and `PipelineRun` never has a default workflow profile.

## Details worth knowing

- **One row per run, or per stage.** With stages set, each matching stage is its own row. A retried stage is a new row.
- **Overlapping subscriptions.** If two subscriptions match the same run, the first one in `config.json` owns the row.
- **Stage and job names.** Azure DevOps matches the stage name from the run's timeline. GitHub has no stage concept in its jobs
  API, so it matches the job name; there is no environment match yet.
- **Cancelled and skipped runs never alert.**
- **Permissions.** No new access: Azure DevOps uses your existing `az` token, GitHub uses your existing `gh` login. Picking a
  pipeline needs read access to builds (ADO) or Actions (GitHub).
- **Turning it off.** Untick a subscription in Settings to pause it, or remove it. Its rows disappear with it.

The `config.json` shape is in [configuration.md](configuration.md#pipelinesubscriptions).
