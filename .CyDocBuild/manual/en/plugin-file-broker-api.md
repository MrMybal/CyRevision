---
id: plugin-file-broker-api
title: Project file access
description: Enumerate, read and write only within the capabilities granted by the host.
section: Developers / Public API
parent: plugin-api
kind: api
order: 150
revision: 1
---

## Operations {#operations}

Import `CyRevision.Plugin.Abstractions`. The host passes `IPluginProjectFileSandbox` during project activation. Its `Policy` describes the project ID/root, plugin ID, permissions, allowed relative roots and maximum read/write sizes. Default byte budgets in `PluginProjectSandboxPolicy` are 64 MiB for each operation.

`EnumerateFilesAsync` needs enumeration permission. `ReadAllBytesAsync` needs read permission. `WriteAllBytesAsync` needs write permission. Supply project-relative paths. Absolute paths, traversal, disallowed roots and links escaping the project are rejected. Writes use atomic replacement.

```csharp
using CyRevision.Plugin.Abstractions;
byte[] data = await files.ReadAllBytesAsync("Config/DefaultEngine.ini", cancellationToken);
```

This fragment assumes the host already granted that relative root and a read budget large enough for the file.

## Boundary {#boundary}

The broker protects cooperative extensions; it is not an OS isolation boundary for an in-process .NET DLL. An in-process plugin remains trusted code. Requesting network/process capabilities does not create a separate protected process.

[Activation](doc:plugin-contracts-api#project-context)
