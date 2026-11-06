---
id: cystore-api
title: CyStore storage contracts
description: Inspect, capture, verify and reconstruct optional chunk-store versions.
section: Developers / Public API
parent: plugin-api
kind: api
order: 150
revision: 1
---

## Experimental storage {#experimental-storage}

`ICyStorePlugin` extends the normal plugin lifecycle. It offers a content-addressed chunk store independently from Git's object database. Ordinary LFS pointers stay compatible, and these methods do not create or rewrite Git commits.

Inspect or initialize the store, capture selected files or tracked LFS files, then list stored versions. The result records distinguish newly written chunks from reused chunks and can report skipped files or warnings.

## Verify before reconstructing {#reconstruct}

Choose a version ID returned by `ListVersionsAsync`. Verify it, reconstruct it and read the returned destination and summary before using the bytes. Do not assume reconstruction replaced a working-tree file. Optional `IProgress<CyStoreProgress>` reports stage, path, byte totals and item totals.

Keep an independent backup while using this Alpha feature.

[CyStore workflow](doc:cystore)
