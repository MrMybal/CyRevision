---
id: vpn
title: WireGuard, services, and Unreal Swarm
description: Create an optional VPN that remains independent from Git and Sync permissions.
section: Workflows / Collaboration
parent: sync-and-peers
order: 31
revision: 2
---

## Independent module {#independent-module}
VPN can run alone or alongside Git and Sync. Joining a VPN never grants repository or synchronized-folder access automatically.

## Backend choice {#backend-choice}
System installation uses WireGuard already installed on the machine. Integrated runtime uses official platform components bundled under VpnRuntime and validates every required file against runtime.json SHA-256 hashes. The choice belongs to the project and can only change while its CyRevision tunnel is stopped.

## Guided computer setup {#guided-computer-setup}
Client-only peers need no inbound firewall or router change. An incoming host opens only its configured WireGuard UDP port. Optional Swarm TCP 8008-8009 and CyRevision control TCP 47831 rules are restricted to the VPN subnet. Diagnose detects the LAN address, gateway and firewall. Windows Defender, UFW and firewalld plans can be applied after explicit administrator confirmation; macOS follows the official application-firewall screen. CyRevision rules use deterministic per-project names and can be removed without touching unrelated rules.

## Router / modem {#router-modem}
Reserve the detected LAN address, then forward one external WireGuard UDP port to the same local port. Never forward Swarm or control ports. Test from another network. A private or 100.64.0.0/10 WAN address may indicate CGNAT and require a public server or a public IP from the provider. CyRevision never enables UPnP automatically.

## Sync-assisted onboarding {#sync-assisted-onboarding}
The current signed invitation or response can be placed in the dedicated Sync exchange. Only signed public VPN payloads are accepted; SHA-256 and signed metadata are checked, secret-shaped fields are rejected, and private keys remain local. Loading a message never joins or accepts it automatically.

## Unreal swarm {#unreal-swarm}
Use stable VPN addresses for the Swarm coordinator and agents. Windows Swarm commonly uses TCP 8008 and 8009; the assistant keeps these ports inside the VPN.

```image
{"id":"vpn","src":"assets/vpn.en.svg","alt":"WireGuard, services, and Unreal Swarm","controls":"minimal"}
```

[See also](doc:members-security)
