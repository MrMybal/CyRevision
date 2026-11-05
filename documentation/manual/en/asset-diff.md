---
id: asset-diff
title: Images and binary asset differences
description: Choose text, image or metadata comparison according to the file.
section: Code & Assets
order: 22
revision: 1
---

## Choose a comparison {#choose-a-comparison}
Open Code & Assets > Asset preview & diff, or select a changed file in Changes/History. Choose reference and candidate where offered. Unified and Side by side compare text; Images / Heatmap compares supported rendered images.

## Interpret correctly {#interpret-correctly}
Text additions/deletions show changed lines, not full syntax semantics. JSON/INI are text even if generated. Long lines need horizontal scrolling; bounded previews report that they are incomplete.

For images, inspect both versions, dimensions and the heatmap. Pan/zoom/fit helps examine details. A heatmap shows pixel differences, not whether an artistic change is correct. Keep the original dimensions in mind.

## Unreal and LFS {#unreal-and-lfs}
An offline .uasset/.umap preview can show package metadata and detected names, not a complete Unreal-rendered scene. Open the owning editor for faithful rendering. A Git LFS pointer is a small text record describing an object, not the object's image or mesh. Retrieve missing payloads explicitly.

Use Open diff in window or Enlarge to keep the comparison beside another tool. Opening a preview is read-only; restoration/export are separate actions. Do not interpret unavailable preview support as a corrupt asset without verifying it in its source application.

```image
{"id": "split-diff-v2", "src": "assets/split-diff-v2.png", "alt": "Images and binary asset differences", "controls": "minimal"}
```

[See also](doc:lfs-time-machine)
