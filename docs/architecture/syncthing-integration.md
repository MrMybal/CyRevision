# Syncthing integration

CyRevision does not reimplement Syncthing's synchronization protocol. It manages an isolated Syncthing
runtime per project and controls it through the loopback-only REST API. This preserves protocol and security
updates while keeping the user experience inside CyRevision.

## Runtime boundary

- dedicated configuration, database, API key, GUI port, and transport port per project;
- automatic runtime resolution from the release bundle, CyRevision's managed data directory, or `PATH`;
- an advanced executable override is available only as a fallback;
- CyRevision starts and stops only the process it owns;
- automatic Syncthing self-upgrade is disabled so CyRevision release checksums stay reproducible.

## Folder modes

CyRevision exposes Syncthing's `sendreceive`, `sendonly`, and `receiveonly` modes. A read-only, backup, or
encrypted-archive membership certificate always constrains the effective mode to receive-only.

## Ignore rules

The editor reads and writes `.stignore` in the synchronized folder root as UTF-8. It follows Syncthing's
native syntax instead of inventing a second incompatible ignore format.

## Licensing

Syncthing is MPL-2.0 software. A redistributed runtime must include the applicable license notices and a way
to obtain the corresponding Syncthing source. CyRevision keeps the Syncthing runtime as a separate program
instead of copying its Go implementation into the .NET assemblies.
