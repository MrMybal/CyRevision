---
id: branches
title: Branches and worktrees
description: Inspect before switching, merging or deleting a branch.
section: Git
order: 9
revision: 1
---

## Inspect versus switch {#inspect-versus-switch}
Open Git > Branches and refresh. Distinguish local from remote-tracking branches. Selecting a row inspects it; switching the working branch is a separate action.

## Start work {#start-work}
1. Fetch remote references.
2. Create a descriptively named branch from the intended revision.
3. Switch and verify the header's branch name.
4. Commit focused changes, then push to the intended remote.

Preserve uncommitted files before switching. The inspector can browse a branch without changing the checked-out files. A historical worktree creates another checkout and consumes disk space. Close dependent editors before removing a worktree.

## Integrate and clean up {#integrate-and-clean-up}
Compare branches before a merge or cherry-pick. Apply dependent commits in order. Resolve or abort an outstanding conflict before another integration.

Delete only a branch you no longer need, never the currently checked-out branch. A merged PR does not prove that every unpushed local commit is disposable. PR > Actions offers confirmed local cleanup when eligible; inspect its target. Local deletion and remote deletion are different operations. Do not force deletion merely because a normal deletion is refused.

```image
{"id": "git", "src": "assets/git.en.svg", "alt": "Branches and worktrees", "controls": "minimal"}
```

[See also](doc:revision-composition)
