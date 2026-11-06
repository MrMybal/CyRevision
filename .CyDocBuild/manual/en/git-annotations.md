---
id: git-annotations
title: Line annotations and authorship
description: Trace a line to its commit and read the surrounding change.
section: Workflows / Git
parent: git-history
order: 10
revision: 2
---

## Open annotations {#open-annotations}
Use Git > Annotations with a tracked text path. Load the context/revision offered by the view. Rows associate text with a commit, author and date.

## Investigate {#investigate}
1. Locate a line or search the file/path.
2. Read the commit identity and message.
3. Open that commit in History for related files and intent.
4. Compare the earlier revision to understand the actual change.

Blame identifies the latest revision responsible for a line; formatting, moved code and merges can affect attribution. It does not prove who originally designed a feature. Binary assets have no meaningful line blame: use Git / LFS file history instead.

If empty, verify Git is active, the repository-relative path exists in the chosen revision and the file is committed. Newly added uncommitted lines may have no committed author. Renamed paths can require separate history inspection. Refresh after a branch change.

```image
{"id":"history-v2","src":"assets/history-v2.png","alt":"Line annotations and authorship","controls":"minimal"}
```

[See also](doc:git-history)
