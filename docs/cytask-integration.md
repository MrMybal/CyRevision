# CyTask work-item integration

The optional `CyRevision.Plugin.CyTask` package connects one CyRevision project to one
CyTask project. It follows the same `IWorkItemIntegrationPlugin` flow as Jira and ClickUp:

- search tickets by key, title, description, or status;
- insert stable `#/tasks/{uuid}` links into commit messages and pull-request drafts;
- detect those links across pull-request discussions;
- after merge, ask for confirmation or automatically apply a configured CyTask completion state.

## Configure

1. In CyTask, create a personal API token with the **write** scope from the API page.
2. In CyRevision, open **Plugins** and enable **CyTask Tickets** for the current project.
3. Open the task picker, set the HTTPS CyTask server URL and the CyTask project UUID.
4. Paste the token for the current window, or place it in `CYTASK_API_TOKEN`.
5. Choose **Test & save settings**.

Only the server URL, project UUID, optional label, and environment-variable name are saved.
The API token is never written to the plugin manifest, project configuration, Git, or commit
messages. Plain HTTP is accepted only for a loopback development server.

CyRevision uses CyTask's optimistic ticket revision when applying a transition. If another
user changed the ticket first, CyTask returns a conflict and CyRevision leaves the newer data
untouched.