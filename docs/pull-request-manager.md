# Pull request manager

CyRevision includes an integrated pull request workspace. The first provider is GitHub and the service boundary is provider-neutral so GitLab, Forgejo, or a decentralized review provider can be added without redesigning the desktop interface.

## Repository connection

The manager reads the `origin` remote of the selected Git project. It supports GitHub HTTPS and SSH remotes, including GitHub Enterprise hosts. For enterprise installations, the API address can be overridden in the pull request toolbar.

Public pull requests can normally be read without authentication. Private repositories and write operations require a GitHub token. CyRevision resolves it from either:

- a masked session-only field; or
- the named environment variable, `GITHUB_TOKEN` by default.

The token is never written to the project, Git repository, or CyRevision settings. Prefer a fine-grained token limited to the repository with pull-request write permission and only the additional permissions your organization requires.

## Workspace

The Pull Requests tab provides:

- open, closed, draft, or merged request lists with distinct colors, ownership markers, and the latest CI state;
- title, author, branches, draft state, review state, and mergeability;
- changed-file statistics and unified patches;
- general comments and submitted reviews;
- pull request creation from a head branch to a base branch;
- comment, approve, request-changes, close, reopen, and merge actions;
- merge, squash, and rebase strategies when allowed by the repository;
- a direct link to the provider page;
- a dedicated CI view with matching workflow runs, jobs, complete searchable logs, all/error/warning filters, a synchronized detached log window, workflow dispatch, failed-job reruns, and cancellation when permitted;
- a non-destructive merge-conflict view that lists conflicting paths without checking out or merging either branch;
- automatic Jira/ClickUp/CyTask link detection in the title, description, commits, comments, and reviews;
- optional confirmed or automatic completion transitions for detected tasks after a successful merge;
- confirmed removal of the merged local head branch after a Git safety analysis.

Line-level review comments are not part of the initial alpha implementation; reviews currently apply to the complete pull request.

## CI, conflicts, and linked tasks

The CI page associates workflow runs by pull-request head branch and commit SHA. Job data remains available even when the forge cannot provide the downloadable log archive. Logs are capped for interactive use, can be filtered to classified error or warning lines, searched by source, text, or line number, and opened in a non-modal synchronized window. Dispatch, rerun, and cancellation actions require the same session-only write token and always target the pull-request branch or selected run.

Conflict inspection fetches disposable private references under `refs/cyrevision/inspect`, analyzes them with `git merge-tree`, and removes those references after inspection. It does not switch branches, touch the index, or write a merge result into the working tree.

When a Jira, ClickUp, or CyTask plugin is enabled for the selected project, CyRevision recognizes provider URLs and task IDs across the complete pull-request discussion. After a successful merge, the per-project policy can ask before applying provider completion transitions, apply them automatically, or leave all tasks unchanged. Provider permissions and workflow-specific transition availability are revalidated for every task.

## Safe checkout and remote changes

Checkout refuses to run while the working tree contains changes. It fetches the pull request head into a dedicated remote-tracking reference and creates or fast-forwards a local `cyrevision/pr-N` branch. It never force-resets the current branch.

Merge, close, reopen, workflow dispatch, CI rerun, and task transitions affect remote services. CyRevision displays a confirmation before these actions because merging can trigger CI, deployment, or other repository automation. Provider errors are sanitized before display so credentials are not exposed.

After a merge, local branch removal is separate and optional. CyRevision first checks whether the branch is current, present in another worktree, merged into the current history, or fully published. The confirmation explains what retains its commits. It never deletes the remote branch.

## Decentralized projects

Git, Git LFS, Sync, backup, and peer exchange remain usable without GitHub. Pull requests are forge-backed review objects, so the current manager is available when a supported forge remote is detected. A future decentralized review adapter can use the same interface without making GitHub mandatory for the rest of CyRevision.
