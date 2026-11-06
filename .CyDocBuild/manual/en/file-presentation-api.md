---
id: file-presentation-api
title: Preview and comparison providers
description: Return UI-neutral text, images or metadata for supported files.
section: Developers / Public API
parent: plugin-api
kind: api
order: 150
revision: 1
---

## Requests and results {#requests-and-results}

Implement `IFilePresentationProvider`. `ProviderId` identifies the provider and `Priority` controls ordering. `FilePreviewRequest` contains project root, relative/absolute file paths and size. `FileDiffRequest` contains project root, relative path and baseline/candidate paths.

Check `CanPreview` or `CanCompare` before creating the result. `FilePresentationResult` describes `Text`, `Image` or `Metadata` presentation, a summary and optional text, image, metadata or baseline/candidate/difference image paths. Return null when no presentation can be supplied.

## Keep capabilities honest {#capabilities}

A text preview is not a semantic parser. A binary package fingerprint is not a rendered scene. A Git LFS pointer is not the full asset. Do not claim a renderer or comparison that the provider cannot actually produce.

[File previews](doc:asset-diff)
