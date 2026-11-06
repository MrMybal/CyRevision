---
id: cystore
title: CyStore package versions
description: Inspect published package versions and file manifests.
section: Extensions
parent: plugins
order: 41
revision: 2
---

## Open CyStore {#open-cystore}
Enable the CyStore provider and open its Alpha workspace for a supported project. Configure the endpoint/project options actually offered, then refresh the version list. Repository versions and application update releases are separate catalogues.

## Inspect a version {#inspect-a-version}
1. Select a version and wait for its manifest/files.
2. Review names, size and metadata before requesting content.
3. Choose the intended destination for any supported download/export.
4. Verify the operation's completion rather than assuming a selected row means installed.

The files list is a manifest, not necessarily a local copy. Availability depends on the configured store and account permissions. An empty list can mean no published version, a filter, a connection failure or insufficient rights; read status before retrying.

## Scope and limits {#scope-and-limits}
CyStore is Alpha. Use only the upload/publish/download operations present in the installed provider; it is not a universal marketplace client. Do not overwrite a working engine plugin merely because a package has a higher-looking label. Check compatibility, preserve the previous working version and test the consuming project.

For updating CyRevision itself, use Help > Check for updates instead.

```image
{"id":"integrations","src":"assets/integrations.en.svg","alt":"CyStore package versions","controls":"minimal"}
```

[See also](doc:plugins)
