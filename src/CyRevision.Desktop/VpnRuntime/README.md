# CyRevision integrated WireGuard runtime

Platform runtime packages are placed in `VpnRuntime/<runtime-identifier>` beside the application.

Required files:

- Windows: the official `wireguard.exe` tunnel manager/service binary and `wg.exe`.
- Linux/macOS: `wireguard-go`, `wg`, and `wg-quick`.
- Every platform: `runtime.json`, containing a `files` object whose values are lowercase SHA-256 hashes.

Example:

```json
{
  "version": 1,
  "files": {
    "wireguard-go": "<sha256>",
    "wg": "<sha256>",
    "wg-quick": "<sha256>"
  }
}
```

CyRevision refuses to execute an incomplete runtime or a binary that does not match its manifest. Runtime binaries are packaged separately so their upstream licenses and platform-specific privilege requirements remain explicit.
