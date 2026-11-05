---
id: diagnostics
title: Performance diagnostics and safe troubleshooting
description: Gather reproducible evidence and reduce unnecessary background work.
section: Support
order: 46
revision: 1
---

## Inspect rather than guess {#inspect-rather-than-guess}
Open Extensions & Help > Diagnostics for current metrics and recent samples. Refresh metrics explicitly; Clear samples affects recorded measurements, not the underlying repository. Console & Logs and Task details provide operation context.

## Reproduce {#reproduce}
1. Record the application version, operating system, theme/density and display scaling.
2. Note the project mode, view, filter and exact action.
3. Wait for the visible loading state or cancel a genuinely stalled cancellable operation.
4. Repeat on a small sample project if possible and compare behavior.

## Improve responsiveness {#improve-responsiveness}
Bound graph/history analysis, narrow searches and avoid repeatedly rescanning generated build output. Prefer exclusions appropriate to the project over disabling all monitoring. Keep cache and build output on disks with sufficient free space. Reduce parallel Unreal builds if CPU/RAM/disk is saturated.

## Report a problem {#report-a-problem}
Include expected versus observed result, a minimal sequence and relevant redacted log excerpts. A screenshot should show the selected view and loading state. Do not delete a repository, backup store or private service profile to fix a UI issue. If the interface appears blocked, check for a confirmation dialog in an auxiliary window and try View > Reset panel layout.

```image
{"id": "settings", "src": "assets/settings.en.svg", "alt": "Performance diagnostics and safe troubleshooting", "controls": "minimal"}
```

[See also](doc:troubleshooting)
