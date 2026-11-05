---
id: tasks
title: CyTask, Jira and ClickUp workflow
description: Configure task providers and verify linked-task transitions after merge.
section: Extensions
order: 42
revision: 1
---

## Enable the provider {#enable-the-provider}
Open Plugins and locate CyTask, Jira or ClickUp. Enable/configure only the installed integration you need. Provider-supplied panels/modes depend on that plugin's capabilities; installing a provider does not connect every project automatically.

## Linked tasks in pull requests {#linked-tasks-in-pull-requests}
PR > Actions can detect supported task URLs/IDs from PR context such as title, description or branch. Inspect the detected task, provider and project before changing its state. An ambiguous ID alone may need an explicit provider/project mapping.

## After merge {#after-merge}
1. Review the detected references and choose the intended transition.
2. Prefer manual confirmation while establishing the workflow.
3. Enable an automatic policy only for a verified project/provider/status mapping.
4. Merge under the repository's normal permissions.
5. Check each task update result separately.

A merged PR and a successful task transition are distinct remote operations. One can succeed while the other fails due to permissions, network, missing task or invalid workflow transition. Refresh/check the task before retrying; do not merge again to trigger a task update.

Do not promise a universal Done status: Jira/ClickUp workflows vary by workspace. CyTask's own data and status model can differ from both.

```image
{"id": "tasks", "src": "assets/tasks.en.svg", "alt": "CyTask, Jira and ClickUp workflow", "controls": "minimal"}
```

[See also](doc:pull-request-manager)
