---
id: safe-lfs-storage
title: Safe LFS storage and cleanup
description: Move the LFS cache to another disk and reclaim old objects without deleting the project's last copy.
section: Workflows / Git
parent: lfs-time-machine
order: 19
revision: 2
---

## Classification {#classification}
Open Git LFS > Storage & safe cleanup and run Analyze. Protected objects are reachable from local refs, stashes, indexes, or worktrees and are never selected. This includes commits and branches that have not been pushed. Eligible objects are orphaned, outside the grace period, and have the configured number of verified copies. Everything else is blocked.

## Retention evidence {#retention-evidence}
A remote copy is accepted only after a Git LFS dry run with remote and unreachable-object verification. A peer copy requires a fresh signed inventory and an object actually published in the exchange store. An archive copy uses CyRevision's manifest and is SHA-256 checked again before deletion. With no remote, retain at least one peer or archive copy; require two or more for stronger redundancy.

## Deleted prs and branches {#deleted-prs-and-branches}
A branch deleted on a forge may still exist locally. Its objects stay protected until you deliberately remove the local ref. Once orphaned, Analyze decides whether another verified copy makes them eligible. A local-only PR or unpushed branch is always kept.

## External storage {#external-storage}
Choose a dedicated empty directory, then relocate. CyRevision copies and hashes every object before setting the repository-local lfs.storage value. A repository ownership marker prevents unsafe shared stores. Keep the old cache for the first test, then enable removal only after checkout and Time Machine access succeed.

## Cleanup {#cleanup}
Archive candidates creates verified copies without deleting. Analyze again, review each decision, then confirm cleanup. Plans expire after fifteen minutes, local references are rechecked, and an audit is written under .git/cyrevision/lfs-cleanup.

```image
{"id":"backups","src":"assets/backups.en.svg","alt":"Safe LFS storage and cleanup","controls":"minimal"}
```

[See also](doc:lfs-time-machine)
