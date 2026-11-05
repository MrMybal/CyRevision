---
id: unreal-builds
title: Local Unreal builds and diagnostics
description: Select target/toolchains, run a bounded build and inspect its output.
section: Extensions
order: 39
revision: 1
---

## Prepare {#prepare}
Enable Unreal integration and select a detected Unreal project. Open Unreal builds. This workspace runs local engine builds; it is distinct from GitHub Actions and remote build agents.

## Configure {#configure}
1. Select the engine version, target, platform and configuration.
2. Set required SDK/NDK, Java or Linux toolchain paths for that platform.
3. Choose output directory, timeout and maximum parallel builds.
4. Enable Cook and package only when that is intended.
5. Review a preset before applying or saving it.

Auto-configuration helps discover tools; it does not install every proprietary SDK or guarantee license acceptance. Verify the engine/platform requirements in the engine installation.

## Execute and inspect {#execute-and-inspect}
Use the offered build action or select a version range for batch validation. Results, Errors and warnings, and Full log are separate tabs. Read the full output around a diagnostic; filtered compiler lines can omit the root cause. A successful build for one engine version does not validate all versions in a range.

Builds can consume substantial CPU, RAM and disk. Keep concurrency conservative and do not delete output/toolchain folders while jobs are active. If a target fails before compilation, check project association, installed components and command output before changing source.

```image
{"id": "ci", "src": "assets/ci.en.svg", "alt": "Local Unreal builds and diagnostics", "controls": "minimal"}
```

[See also](doc:remote-builds)
