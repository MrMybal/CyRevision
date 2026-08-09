# Initial architecture

## Product profiles

The user interface exposes friendly presets, while the core stores independent feature flags.

| Preset | Git | LFS | Peer sync | Backup |
| --- | --- | --- | --- | --- |
| Git only | Yes | Optional | No | Optional |
| Git + peer sync | Yes | Optional | Yes | Optional |
| Sync only | No | No | Yes | No |
| Sync + versions | No | No | Yes | Yes |
| Backup only | No | No | No | Yes |

Standard Git remotes are independent from peer synchronization. GitHub, GitLab, Forgejo, SSH remotes and fully local repositories remain compatible but optional.

## Synchronization boundary

Syncthing is a transport, not the source of truth. CyRevision exchanges immutable, signed transactions in a dedicated exchange directory. Working trees and active `.git` directories are never synchronized directly.

The CyRevision-managed Syncthing process must use:

- a private configuration directory;
- a private index database;
- a separate device identity;
- dynamically selected non-conflicting ports;
- an API bound to loopback only;
- a process ownership record so only the process started by CyRevision can be stopped.

## Peer admission

A Syncthing device ID authenticates a device connection. CyRevision adds project-level authorization:

1. An administrator creates an expiring, single-use invitation.
2. The recipient presents a user identity and a device identity.
3. Both sides verify a short confirmation code.
4. An administrator signs a membership certificate with an assigned role.
5. Transactions are accepted only from active, authorized signing keys.
6. Revocation blocks future transactions and starts a project-key rotation.

Revocation cannot erase data already downloaded by a former member.

## Backup boundary

Synchronization mirrors the current state; backups retain historical states. Backup manifests and content-addressed chunks are stored outside synchronized working folders. Retention can be current-only, time-limited, version-limited, timeline-based or permanent.

