# Historical branches without switching the main repository

The **Git > Branches** workspace can create a branch from any commit in an isolated Git worktree. This is the recommended way to compile or inspect an old version while the main project contains current work.

1. Select a branch and then a commit in its history.
2. Enter a new branch name.
3. Keep **Isolated worktree (recommended)** enabled.
4. Choose **Create from selected commit**.

CyRevision creates the branch in a sibling directory below `.cyrevision-worktrees/<repository>`. The current branch, index and working files of the main repository are not switched or rewritten. The managed worktrees appear below the branch history with their branch, commit and state; they can be opened in the file manager.

Removal is deliberately conservative. The normal **Remove** action uses `git worktree remove` without `--force`, so Git refuses to delete a worktree that still contains local changes. CyRevision will only manage and remove paths inside its own `.cyrevision-worktrees` root.

These worktrees are working copies, not backups. Important results still need to be committed, copied to a build archive or backed up before cleanup.
