# Syncthing runtime slot

CyRevision detects a Syncthing executable in this order:

1. a platform folder here (`win-x64`, `linux-x64`, `linux-arm64`, `osx-x64`, or `osx-arm64`);
2. the per-user managed runtime directory;
3. the system `PATH`;
4. an optional project-specific advanced override.

Official Syncthing release packages can be placed in the platform folder with the executable named
`syncthing.exe` on Windows or `syncthing` on Linux and macOS. Release packaging must verify the official
GitHub release SHA-256 digest before adding a binary.

Syncthing is licensed under MPL-2.0. Keep its license and source-code offer with every redistributed binary.
