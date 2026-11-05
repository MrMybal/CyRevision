---
id: ci-actions
title: CI runs and full logs
description: Search full build output, filter errors/warnings and manage eligible runs.
section: Git
order: 14
revision: 1
---

## Open a run {#open-a-run}
Open Git > CI / Actions, refresh workflows/runs and select one. Jobs load separately. Compare branch, revision and timestamps: an older successful run can coexist with the latest queued/running build.

## Read the complete log {#read-the-complete-log}
1. Select a run and job, then open Full log / Logs.
2. Wait for download/extraction.
3. Choose All, Errors or Warnings. Search combines with severity filtering.
4. Use horizontal scrolling or open the enlarged log window for long compiler messages.

Source and line numbers remain visible. Classification is a text heuristic; an empty Errors filter does not prove success. Return to All for context and read the final conclusion. Active, expired or restricted logs can be unavailable; Error summary is not the full log.

## Dispatch, rerun and cancel {#dispatch-rerun-and-cancel}
Choose workflow and Git ref before dispatch. The workflow must support manual dispatch. Rerun failed applies to eligible completed runs. Cancel requires host Actions/write permission. A job's context menu can cancel the parent run, not just that individual job; read the confirmation. Actions are subject to server authorization even if displayed locally.

PR > CI provides the same log reader/search/filters. Refresh after new commits. Queued or running checks must not be interpreted as success.

```image
{"id": "ci", "src": "assets/ci.en.svg", "alt": "CI runs and full logs", "controls": "minimal"}
```

[See also](doc:pull-request-manager)
