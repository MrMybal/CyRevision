---
id: work-item-api
title: Issue tracker contracts
description: Connect project-scoped Jira, ClickUp or CyTask providers and apply explicit transitions.
section: Developers / Public API
parent: plugin-api
kind: api
order: 150
revision: 1
---

## Connection {#connection}

Implement `IWorkItemIntegrationPlugin` in `CyRevision.Plugin.Abstractions`. It also implements the plugin lifecycle. `Provider` exposes names and configuration labels. `WorkItemConnectionSettings` contains `ProjectId`, `BaseUrl`, `ScopeId`, `AccountName` and `TokenEnvironmentVariable`; it deliberately has no token property.

Pass session credentials only to the operation that needs them. Search and resolve return `WorkItemReference` values: provider identity, task ID/key, title, status and URL.

## Read, then transition {#transitions}

1. Load and test the project/provider connection.
2. Search or resolve the intended task.
3. Read transitions offered for its current workflow.
4. Apply the selected transition only after the host's configured/manual decision.
5. Inspect the previous and new state in the result.

A successful PR merge is not proof that the task transition succeeded. Provider permission, workflow and network errors remain separate. Do not invent a universal completion-state name.

[Task workflow](doc:tasks)
