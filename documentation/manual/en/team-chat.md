---
id: team-chat
title: Project chat, channels, and private server
description: Discuss in project channels over a VPN, synchronized folder, or your own CyRevision server.
section: Team and network
order: 33
revision: 1
---

## Channel workspace {#channel-workspace}
The Team chat tab uses project channels, a compact message timeline, attachments, delivery state, and online-member presence. General, development, and builds are available by default. A private server can create additional persistent channels.

## Transports {#transports}
VPN connects directly to a CyRevision host over a loopback, LAN, or WireGuard address. Sync folder writes immutable per-message files into a synchronized directory and avoids shared-log conflicts. Private server uses the authenticated CyRevision.Server API and keeps the discussion available when team computers are offline.

## Private server {#private-server}
Install CyRevision.Server on a machine you control, set CYREVISION_SERVER_TOKEN, and publish its ASP.NET Core endpoint behind HTTPS. Enter the server URL and token in the project chat settings. Plain HTTP is refused unless the option is explicitly enabled and the address resolves to loopback, LAN, carrier-grade NAT, or a VPN range.

## Files and history {#files-and-history}
Attachments are size-limited and verified with SHA-256 when stored and downloaded. Server and Sync messages are immutable files grouped by project. Local and synchronized archives can be encrypted. Conversation retention and the attachment limit remain configurable per project.

## Security {#security}
Do not publish the direct VPN chat TCP port on a router. For an Internet-facing server, terminate TLS with a trusted certificate and protect the server token like a password. Chat access does not grant Git, Sync, backup, or VPN permissions automatically.

```image
{"id": "sync", "src": "assets/sync.en.svg", "alt": "Workflow overview", "controls": "minimal"}
```

[See also](doc:members-security)
