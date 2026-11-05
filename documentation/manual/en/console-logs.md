---
id: console-logs
title: Console, logs and operation status
description: Distinguish application diagnostics, Git commands and build output.
section: Code & Assets
order: 23
revision: 1
---

## Where to look {#where-to-look}
Code & Assets > Console & Logs contains repository command output and application events. CI build logs live in CI / Actions or PR > CI; Unreal local build logs live in Unreal builds. Choosing the correct source avoids confusing an application warning with a compiler failure.

## Investigate {#investigate}
1. Note the selected project and the time of the problem.
2. Filter events by text/severity where offered.
3. Inspect the related operation and its final state.
4. Copy only the relevant excerpt for a support report.

The bottom status bar shows the current operation. Task details opens active/recent operations; background updates should not force that panel open. A cancelled scan superseded by newer filesystem events is different from a failed write.

## Repository console {#repository-console}
Commands run in the selected repository and can modify files/history. Review commands and parameters; never paste an unexplained destructive command from a log or message. Cancellation requests do not undo a command that already finished.

Logs can contain paths, command arguments and remote output. Redact access credentials and unrelated work before sharing. Clear the view only after preserving the evidence needed to reproduce the issue.

```image
{"id": "ci", "src": "assets/ci.en.svg", "alt": "Console, logs and operation status", "controls": "minimal"}
```

[See also](doc:diagnostics)
