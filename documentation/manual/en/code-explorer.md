---
id: code-explorer
title: Explorer, previews and text editing
description: Browse project files and understand preview limits.
section: Code & Assets
order: 20
revision: 1
---

## Explore {#explore}
Open Code & Assets > Solution Explorer. Search a filename or path, choose Tree/List and optionally show hidden files. Refresh explicitly when needed; automatic indexing excludes dependency caches, .git and common generated outputs. A filtered result is not a complete disk inventory.

## Preview a file {#preview-a-file}
Select a row. Text formats including JSON and INI should open as text, with syntax-aware display where supported. Large files use bounded previews and report truncation; the visible excerpt is not the entire file. Binary files show metadata or an appropriate asset preview rather than arbitrary decoded bytes.

## Edit deliberately {#edit-deliberately}
Use the editing control offered by the view, inspect the draft and apply/save it explicitly. Changing tabs or a preview selection is not a commit. Check Git > Changes afterward and run format/build checks in the owning tool. Do not assume cross-file Undo or a saved version exists unless the editor provides it.

## File actions {#file-actions}
Right-click the selected file for Git / LFS history when Git is active, or Sync history in a supported Sync project. Folder and file histories have different scopes. A selected asset's preview is not its author history; history opens separately. If an action asks you to select a file, reselect the actual file row rather than a directory heading.

```image
{"id": "split-diff-v2", "src": "assets/split-diff-v2.png", "alt": "Explorer, previews and text editing", "controls": "minimal"}
```

[See also](doc:code-and-ai)
