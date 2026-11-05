---
id: file-locks
title: Locks and work-in-progress reservations
description: Server locks are different from advisory team notices.
section: Git LFS
order: 16
revision: 1
---

## LFS locks {#lfs-locks}
Open Git > File locks and refresh the server list. Read path, owner, date and whether a lock is yours. Cached entries do not establish that the server is currently reachable.

## Coordinate an edit {#coordinate-an-edit}
1. Check the current lock before editing a shared binary asset.
2. Lock the exact path if your workflow requires it.
3. Edit/test, then commit and push normally.
4. Unlock when the team can safely resume work.

Unlocking another user's file or all locks needs appropriate authorization. Coordinate first and inspect the confirmation. A commit does not automatically release every remote lock. Enforcement depends on host capabilities and the team's tools.

## Advisory reservations {#advisory-reservations}
Team & Network > Work in progress and Unreal Content Browser markers communicate that someone is working on a path. They do not change filesystem permissions, create an LFS lock or prevent writes. Use them for coordination, never as proof of exclusive access.

Refresh stale ownership before overriding a lock. If authorization is refused, resolve account/server permissions rather than deleting unrelated local metadata.

```image
{"id": "locks", "src": "assets/locks.en.svg", "alt": "Locks and work-in-progress reservations", "controls": "minimal"}
```

[See also](doc:lfs-time-machine)
