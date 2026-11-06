---
id: git-graphs
title: Commit network and repository graphs
description: Explore history topology and sampled file relationships.
section: Workflows / Git
parent: git-history
order: 15
revision: 2
---

## Analyze {#analyze}
Open Git > Git graphs, choose commit/file limits and whether to include all branches, then Analyze repository. Start with modest limits on a large repository; these bound work and memory.

## Interpret the views {#interpret-the-views}
Commit network shows ancestry and merge lanes. File relations show files changed together in the analyzed sample, not necessarily runtime dependencies. Git activity counts describe that sample, not individual productivity. Unreal dependency results depend on available metadata and parser support, not a full engine load.

## Navigate {#navigate}
Drag to pan. Wheel scrolls vertically; Shift+wheel horizontally and Ctrl+wheel zooms where indicated. Fit or double-click returns the graph to view. Select a node or row to inspect commit metadata. Resize a cramped window.

This local analysis is read-only: it does not fetch, modify the index or rewrite history. If the graph appears empty, check commit count, branch scope and filters, then Fit/reanalyze. A repository without commits has no commit topology.

```image
{"id":"git","src":"assets/git.en.svg","alt":"Commit network and repository graphs","controls":"minimal"}
```

[See also](doc:git-history)
