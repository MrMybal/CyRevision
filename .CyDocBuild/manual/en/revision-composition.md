---
id: revision-composition
title: Multi Restore and cherry-pick composition
description: Compose file versions or ordered commits with a safety preview before changing the working tree or a local branch.
section: Workflows / Git
parent: git-history
order: 11
revision: 2
---

## Multi restore {#multi-restore}
Open Compose > Multi restore, choose a commit, and load its changed files. Check only the files to include. BeforeCommit selects the state immediately before that commit; AtCommit selects the version stored by the commit. Adds, deletes, and renames become explicit restore or delete operations.

## Safety preview {#safety-preview}
Build safety preview before applying. The preview expires after fifteen minutes, verifies that HEAD has not moved, detects local changes, and blocks missing Git LFS objects. Application writes a timestamped recovery copy under the Git common directory before changing any path. The index is not modified and no commit or push is created.

## Branch comparison {#branch-comparison}
Open Branch compare & cherry-pick, choose a source and an existing local target. Source-only, target-only, and patch-equivalent commits are shown separately, so an already applied change can be recognized even when its hash differs. Selected source-only commits are applied in the displayed order.

## Cherry-pick modes {#cherry-pick-modes}
KeepCommits preserves individual commits. CombineIntoOne applies their content and creates one commit with the supplied message. Merge commits are blocked until an explicit mainline workflow is available.

## Isolation and conflicts {#isolation-and-conflicts}
If the target is not the active branch, CyRevision uses a temporary worktree and leaves the displayed branch untouched. A conflict aborts the operation and restores the target tip. Nothing is pushed automatically.

## Detached windows {#detached-windows}
View > Open detached workspace opens additional History, Code, Multi Restore, or Cherry-pick windows. Open several at once for multiple monitors; their project data and selection stay linked to the main window.

```image
{"id":"git","src":"assets/git.en.svg","alt":"Multi Restore and cherry-pick composition","controls":"minimal"}
```

[See also](doc:merge-conflicts)
