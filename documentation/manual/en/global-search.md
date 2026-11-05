---
id: global-search
title: Search code and line history
description: Find text across files with explicit search scope.
section: Code & Assets
order: 21
revision: 1
---

## Open and scope {#open-and-scope}
Press Ctrl+Shift+F or open Code & Assets > Code search. Enter text and restrict files with semicolon-separated glob patterns such as *.cs;*.cpp. Use case-sensitive, whole-word or regular-expression matching only when appropriate.

## Follow a result {#follow-a-result}
1. Run the search and wait for completion.
2. Select a result to open the file and matching line.
3. Inspect surrounding code, not just the matched fragment.
4. Select a line range for History of selection where available; use file history for whole-file changes.

ripgrep is used when available with a managed fallback. Generated/dependency folders can be excluded by indexing rules. An empty result means no match in the searched scope, not necessarily no matching bytes anywhere on disk. Check patterns, casing, hidden files and exclusions.

## Regular expressions {#regular-expressions}
Start with a literal query before a complex expression. Invalid expressions should be corrected rather than repeatedly retried. Searching is read-only; replacing/editing is a separate operation and must be reviewed before saving. After a branch switch or external edit, refresh indexing if results refer to older content.

```image
{"id": "workspace-v2", "src": "assets/workspace-v2.png", "alt": "Search code and line history", "controls": "minimal"}
```

[See also](doc:code-explorer)
