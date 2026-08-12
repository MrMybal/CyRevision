# Remote build agent

`CyRevision.Build.Agent` runs beside the desktop application or on a Windows, Linux, or macOS CI/build machine. It does not require GitHub and never pushes a temporary branch.

## First start

Windows example from an installed release:

```powershell
& 'C:\Program Files\CyRevision\BuildAgent\CyRevision.Build.Agent.exe' --print-token
```

Linux package example:

```bash
cyrevision-build-agent --print-token
```

The first start creates a random 256-bit bearer token and a safe empty `agent.json`. Keep the token secret. The agent refuses all builds until a project and at least one recipe are configured locally.

## Example `agent.json`

```json
{
  "jobsRoot": "D:/CyRevisionBuildJobs",
  "maximumParallelJobs": 1,
  "completedJobRetentionHours": 72,
  "projects": [
    {
      "projectId": "00000000-0000-0000-0000-000000000000",
      "projectName": "My Unreal Project",
      "workspaceRoot": "D:/BuildWorkspaces/MyProject",
      "allowUploadedSnapshots": true,
      "maximumSnapshotBytes": 107374182400,
      "recipes": [
        {
          "id": "unreal-win64-development",
          "displayName": "Unreal Win64 Development",
          "executable": "C:/Windows/System32/cmd.exe",
          "arguments": ["/d", "/s", "/c", "\"C:/Epic/UE_5.5/Engine/Build/BatchFiles/Build.bat\" MyProjectEditor Win64 Development -Project=MyProject.uproject -WaitMutex"],
          "workingDirectory": ".",
          "artifactPatterns": ["Binaries/Win64/**", "Saved/Logs/**"],
          "timeoutMinutes": 180
        }
      ]
    }
  ]
}
```

Copy the exact project ID shown in CyRevision's **Remote builds** tab. The executable, arguments, timeout, workspace and artifact patterns are controlled only by the build-machine operator. A client can select a recipe ID but cannot submit a command.

## Network setup

For local testing the agent listens on `127.0.0.1:47841`. For another VPN peer, bind to the build machine's WireGuard IPv4 and add `--allow-private-http`, then enable **Remote build agent over VPN** in the firewall assistant. Do not create a modem/router forward for TCP 47841. Use HTTPS as well when transport leaves the trusted WireGuard tunnel.

## Source modes

- **ExistingWorkspace:** no source upload. The agent uses `workspaceRoot` and rejects a different Git HEAD when both sides expose a Git repository. Use CyRevision Sync or another deliberate deployment step first.
- **UploadedSnapshot:** tracked plus non-ignored working files are zipped. `.git`, `Binaries`, `Intermediate`, `Saved`, `DerivedDataCache`, `bin`, `obj`, `.idea`, `.vs`, and `node_modules` are excluded. The agent extracts into a unique job directory with traversal and size checks.

After success, only files matched by `artifactPatterns`, plus `build.log`, are placed in the downloadable artifact ZIP. Jobs can be cancelled from CyRevision and each recipe has a hard timeout.
