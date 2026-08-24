# CyStore Alpha

CyStore is an optional, project-scoped storage plugin. It adds the separate **Git + CyStore — ALPHA** operating mode to an existing Git repository.

The Alpha implementation is deliberately additive:

- Git remains the source of truth for commits, branches and remotes;
- standard Git LFS pointers remain unchanged and compatible with other Git clients;
- CyStore never initializes a Git repository implicitly;
- capture, verification and reconstruction are explicit user actions;
- reconstructed files are written below `.cyrevision/cystore/restored`, never over the working file;
- the whole `.cyrevision/` directory is added to the repository's local `.git/info/exclude`, not to the shared `.gitignore`.

## Storage format

Hydrated files are split using deterministic content-defined chunking. The current Alpha format uses:

- 1 MiB minimum chunk size;
- 4 MiB target chunk size;
- 8 MiB maximum chunk size;
- SHA-256 identities for every chunk and complete file;
- immutable chunk files and JSON manifests;
- atomic metadata writes.

A small change normally creates only a few new chunks. Chunks shared with earlier versions or other captured files are reused.

## Enable the mode

1. Open an existing Git repository in CyRevision.
2. In **Extensions & Help → Plugins**, enable **CyStore Alpha** for this project.
3. In **Overview → Project**, choose **Git + CyStore — ALPHA** and apply the mode.
4. Open the **CyStore ALPHA** workspace.
5. Select **Initialize CyStore**. This only creates `.cyrevision/cystore` and the local exclusion.
6. Select **Capture hydrated Git LFS** when the required LFS files are present locally.

The version table can then verify every chunk or reconstruct a selected version into the restore directory.

## Current Alpha limits

This first milestone is a local experimental store, not a replacement remote protocol. It does not yet:

- publish CyStore manifests or chunks to peers or servers;
- replace Git LFS pointer files;
- garbage-collect unreferenced chunks;
- transparently hydrate files during checkout;
- modify commits, branches or the Git index.

These constraints keep the feature reversible and allow ordinary Git and Git LFS clients to continue working. A future protocol can add signed inventories, peer exchange and remote storage without changing existing Git history.

## Safety

CyStore is marked **ALPHA** in the plugin catalogue, project mode, workspace and documentation. Keep an independent backup and do not treat the local chunk store as the only copy of production assets.
