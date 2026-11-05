---
id: ignore-lfs-rules
title: Ignore rules and tracking patterns
description: Separate generated files, prepared changes and large-file tracking.
section: Git LFS
order: 17
revision: 1
---

## Two policies {#two-policies}
Git > Ignore rules handles untracked paths to exclude. Git > Git LFS manages patterns in .gitattributes for content you do want to version. Ignoring and LFS tracking are not interchangeable.

## Ignore generated files {#ignore-generated-files}
1. Identify engine caches, temporary files and build output.
2. Add repository-relative patterns, preferably scoped directories.
3. Inspect matching paths, save and review the .gitignore diff.
4. Commit the shared policy only when intended.

Ignoring a previously tracked file does not remove it from the index. Removing tracked generated content is a separate team-visible change; review its scope before committing.

## LFS patterns {#lfs-patterns}
Add patterns before committing large assets and confirm Git LFS is installed. Review .gitattributes. Adding a pattern does not migrate old payloads or rewrite history; history migration requires separate coordinated maintenance.

The Changes view's Keep/local-only preparation can exclude items from a prepared commit without deleting them. That choice is not automatically a shared ignore rule. Inspect the included set and staged index before Commit.

```image
{"id": "git", "src": "assets/git.en.svg", "alt": "Ignore rules and tracking patterns", "controls": "minimal"}
```

[See also](doc:safe-lfs-storage)
