---
id: application-updates
title: Stable release updates
description: Review, download, verify, and install a published CyRevision release from an in-app prompt.
section: Get started
parent: getting-started
order: 45
revision: 2
---

## Releases only {#releases-only}
The updater reads the latest stable GitHub Release for CyRevision. Commits, branches, drafts, and prereleases are never offered as application updates. Project repositories remain independent and do not require GitHub.

## Update prompt {#update-prompt}
CyRevision checks after startup and displays a prompt when a newer stable version exists. The prompt includes the release notes and offers Later, View release, and Download and install. Check for updates remains available at any time.

## Platform package {#platform-package}
CyRevision selects the native installer for the current system: Windows Setup, Linux DEB for x64 or ARM64, or macOS DMG for x64 or ARM64. The release page remains available when a compatible package is not present.

## Safe installation {#safe-installation}
The package is downloaded into the CyRevision cache. Its SHA-256 value must match the release's SHA256SUMS.txt manifest. CyRevision then schedules an external launcher, stops its background services, and closes completely. The launcher waits for the process to exit before opening the installer. No source-code git pull is performed.

```image
{"id":"updates","src":"assets/updates.en.svg","alt":"Stable release updates","controls":"minimal"}
```

[See also](doc:tray-and-startup)
