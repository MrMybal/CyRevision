# Safe Git LFS storage management

CyRevision treats local deletion as a last step, never as the first storage optimization.

## Safety classes

- **Protected:** the object is reachable from a local ref, stash, current index, or another worktree. This includes local-only branches and therefore unpublished pull-request work.
- **Eligible:** the object is no longer referenced locally, is older than the grace period, and has the configured number of verified copies.
- **Blocked:** no sufficient remote, peer, or archive evidence exists. CyRevision assumes this machine may hold the last copy.

Remote evidence is collected with a dry run using Git LFS remote verification, including unreachable objects. Peer evidence comes from fresh signed CyRevision inventories and is accepted only when the object is actually published in the exchange store. Archive evidence requires the CyRevision manifest, matching size, and SHA-256 verification again at deletion time.

## Deleted branches and pull requests

Deleting a branch on a forge does not immediately remove its local ref. Fetch/prune and deliberately remove the local branch only after the work is no longer needed. As long as the local ref exists, its LFS objects are protected. Once the object is orphaned, **Analyze** shows whether a remote, peer, or archive copy makes it eligible.

With no Git remote, configure at least one fresh published peer or archive copy. Increase **Required copies** to two or more when the project needs redundancy across several independent devices.

## External storage

Select a dedicated empty directory and use **Relocate and activate external storage**. CyRevision copies the complete LFS store, verifies object names against SHA-256, writes `.cyrevision-lfs-owner.json`, and only then runs `git config --local lfs.storage <path>`. The ownership marker prevents two repositories from sharing one custom store, a layout Git LFS explicitly warns against pruning.

Keep the old cache during the first relocation test. After `git lfs env`, checkout, and Time Machine access work, repeat with removal enabled if disk space must be reclaimed.

## Cleanup audit

Plans expire after fifteen minutes. Execution re-enumerates protected objects, re-hashes local data and archive copies, and skips anything newly referenced. The audit is written under `.git/cyrevision/lfs-cleanup`.
