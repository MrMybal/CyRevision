---
id: git-repository
title: Repository configuration and remotes
description: Configure the hosting connection without leaving the interface.
section: Git
order: 6
revision: 1
---

## Location and prerequisites {#location-and-prerequisites}
Select a Git project and open Overview > Git repository. Review the root, current branch, repository state and remote list. Registering a folder is not initializing Git.

## Add a remote {#add-a-remote}
1. Enter its name, usually origin.
2. Paste the HTTPS or SSH clone URL supplied by the hosting service.
3. Use Add remote, refresh and verify the fetch/push URLs.
4. Fetch before choosing an upstream or publishing work.

The URL identifies the repository; credentials or SSH authenticate separately. Do not embed tokens/passwords in URLs. Recognized hosting URLs can open a repository web page. Other hosts or custom URLs may require opening the site manually.

## Modify and remove {#modify-and-remove}
Select the exact remote before editing its URL or removing it. Removing the configuration does not delete the hosted repository, but can affect upstream tracking. Recheck branches afterward.

## Fetch, Pull and Push {#fetch-pull-and-push}
Fetch updates remote references without merging. Pull integrates the upstream into the current branch. Push publishes commits and can be rejected by authentication, branch protection or divergence. Review the error rather than force-pushing. A local-only Git project remains usable without any remote.

```image
{"id": "git", "src": "assets/git.en.svg", "alt": "Repository configuration and remotes", "controls": "minimal"}
```

[See also](doc:git-workflow)
