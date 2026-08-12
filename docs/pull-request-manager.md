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

- open, closed, or complete request lists;
- title, author, branches, draft state, review state, and mergeability;
- changed-file statistics and unified patches;
- general comments and submitted reviews;
- pull request creation from a head branch to a base branch;
- comment, approve, request-changes, close, reopen, and merge actions;
- merge, squash, and rebase strategies when allowed by the repository;
- a direct link to the provider page.

Line-level review comments are not part of the initial alpha implementation; reviews currently apply to the complete pull request.

## Safe checkout and remote changes

Checkout refuses to run while the working tree contains changes. It fetches the pull request head into a dedicated remote-tracking reference and creates or fast-forwards a local `cyrevision/pr-N` branch. It never force-resets the current branch.

Merge, close, and reopen affect the remote forge. CyRevision displays a confirmation before these actions because merging can trigger CI, deployment, or other repository automation. Provider errors are sanitized before display so credentials are not exposed.

## Decentralized projects

Git, Git LFS, Sync, backup, and peer exchange remain usable without GitHub. Pull requests are forge-backed review objects, so the current manager is available when a supported forge remote is detected. A future decentralized review adapter can use the same interface without making GitHub mandatory for the rest of CyRevision.
