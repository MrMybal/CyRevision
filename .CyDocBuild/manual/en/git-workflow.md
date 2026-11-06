---
id: git-workflow
title: Everyday Git workflow
description: Review changes, create commits, work with branches, and inspect history without leaving CyRevision.
section: Workflows / Git
order: 7
revision: 2
---

## Refresh and review {#refresh-and-review}
Refresh reads the repository status. Select a changed file to inspect its diff before staging it. Binary and LFS files are identified explicitly.

## Create a revision {#create-a-revision}
1. Review the changed files.
2. Stage all changes or use the repository tools for a narrower selection.
3. Enter a clear commit message.
4. Create the revision.

## Branches and remotes {#branches-and-remotes}
Create or switch local branches from the Branches panel. Fetch downloads remote references without modifying the working tree. Pull integrates the configured remote branch. Push publishes local commits to the standard remote.

## History explorer {#history-explorer}
Search commits by message, author, or hash. Select two revisions for an A ↔ B comparison. Exporting a historical file writes a separate copy and never changes the working tree.

```image
{"id":"git","src":"assets/git.en.svg","alt":"Everyday Git workflow","controls":"minimal"}
```

[See also](doc:git-history)
