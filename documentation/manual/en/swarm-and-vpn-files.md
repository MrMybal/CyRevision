---
id: swarm-and-vpn-files
title: Swarm sessions and secure VPN files
description: Configure Unreal Swarm over WireGuard and exchange files without exposing a public file share.
section: VPN and Unreal Engine
order: 32
revision: 1
---

## Swarm session {#swarm-session}
Open Swarm over VPN after configuring the project tunnel. Choose Agent or CoordinatorAndAgent, select the matching Unreal executables and existing SwarmAgent.Options.xml, then set coordinator VPN IPv4, local alias, groups, agent filter and optional cache. Apply Agent configuration closes no process: close Swarm Agent yourself first. CyRevision creates a timestamped backup and edits only known fields already present in the XML.

## Dns and firewall {#dns-and-firewall}
Apply local DNS alias replaces only a marked block belonging to this project in the Windows hosts file. The firewall assistant creates deterministic TCP 8008-8009 rules limited to the WireGuard subnet. Never forward Swarm ports on the modem/router.

## Full diagnosis {#full-diagnosis}
Run full diagnosis checks Windows support, the assigned VPN address, executable paths, Agent XML, DNS resolution, project firewall, and both Coordinator ports. Every warning or failure includes a concrete repair step. CyRevisionUnreal also provides Tools > Swarm over VPN for autonomous configuration, process launch and port tests.

## Vpn file endpoint {#vpn-file-endpoint}
VPN files binds only to the project WireGuard IPv4 and refuses source addresses outside the project CIDR. Every command requires the 256-bit project token. Choose a private inbox and an explicit shared root, then grant receive, browse and download separately. Send the token to authorized peers through a different trusted channel.

## Transfer safety {#transfer-safety}
Uploads never overwrite an existing file. Uploads and downloads verify the announced size and SHA-256 before an atomic move. Partial files are removed after failure. Absolute paths, parent traversal, junctions and symbolic links cannot escape the shared root. Rotate token immediately revokes old copies and stops the endpoint until restarted.

```image
{"id": "vpn", "src": "assets/vpn.en.svg", "alt": "Workflow overview", "controls": "minimal"}
```

[See also](doc:vpn)
