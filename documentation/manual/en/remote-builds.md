---
id: remote-builds
title: Remote builds over VPN
description: Run allowlisted builds on a CI machine and download artifacts without pushing a temporary branch.
section: Build and VPN
order: 34
revision: 1
---

## Agent setup {#agent-setup}
Install the packaged BuildAgent on the CI/build machine and run CyRevision.Build.Agent --print-token. The first run creates a token and a safe empty agent.json. Add the exact CyRevision project ID and one or more recipes. The agent operator owns the executable, arguments, timeout, workspace, and artifact patterns; a client can select only a recipe ID and can never submit a command.

## Network {#network}
Local testing uses 127.0.0.1:47841. For another peer, bind to the build machine's WireGuard IPv4, add --allow-private-http, and enable Remote build agent over VPN in the firewall assistant. Never forward TCP 47841 on the router.

## Source modes {#source-modes}
ExistingWorkspace transfers no source and uses the configured workspace. If both sides have Git metadata, a different HEAD is rejected. UploadedSnapshot sends tracked and non-ignored working files but excludes .git and generated caches, then extracts into a unique path with traversal and size checks.

## Run and download {#run-and-download}
In Remote builds, enter the VPN endpoint, token, recipe ID, mode, and artifact destination. Test agent lists the allowlisted recipes. Start remote build follows the job log, supports cancellation, and downloads an artifact ZIP after success. Only recipe-declared files and build.log are returned; no commit or branch is pushed to a remote.

```image
{"id": "ci", "src": "assets/ci.en.svg", "alt": "Workflow overview", "controls": "minimal"}
```

[See also](doc:unreal-builds)
