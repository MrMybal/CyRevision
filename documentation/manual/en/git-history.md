---
id: git-history
title: Commit and file history
description: Trace authors, inspect LFS metadata and export historical files.
section: Git
order: 8
revision: 1
---

## Commit history {#commit-history}
Open Git > History and search by message, author or hash. Select a revision: the middle panel lists its files and the right panel shows the selected difference. Wait for dependent panels to finish loading after changing the revision.

## File history, including LFS {#file-history-including-lfs}
1. Open Code & Assets > Solution Explorer.
2. Select the exact file or asset row.
3. Right-click > Git / LFS file history.
4. In the separate window select a commit for author, date, full message and storage details.

LFS versions show pointer OID and expected size. Metadata browsing does not download all historical assets. A missing payload must be obtained explicitly before an export/preview requiring its bytes. Binary assets have commit history, not meaningful line-by-line blame.

## Compare or restore {#compare-or-restore}
Choose another revision for an A-to-B comparison. Export creates a separate file. Restore changes the working file: preserve local edits, inspect the destination and commit deliberately afterward.

Resize/reorder columns and scroll horizontally to read long values. An empty history can mean a never-committed file, another branch or an earlier renamed path. Verify the selected repository-relative path before retrying.

```image
{"id": "history-v2", "src": "assets/history-v2.png", "alt": "Commit and file history", "controls": "minimal"}
```

[See also](doc:lfs-time-machine)
