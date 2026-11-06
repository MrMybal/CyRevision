---
id: server-api
title: Server HTTP API
description: Use authenticated project, Git, backup, peer, VPN and chat endpoints.
section: Workflows / Collaboration
parent: server-setup
order: 145
revision: 1
---

## Authentication and responses {#authentication}

GET /health and GET /api/v1/capabilities do not require an API token. Other /api/v1 routes need the Authorization: Bearer <token> header. This token administers the server; it is separate from signed project membership and WireGuard identity.

Requests/responses use JSON and enum names as strings. Project and peer route IDs are GUIDs. An invalid token returns 401. Known missing-item, denied and invalid-operation errors map to 404, 403 and 400; other failures return 500 problem details. Read the operation result too.

## Routes {#routes}

Add /api/v1 before each path below. Input names refer to JSON properties unless identified as query parameters.

| Method | Path | Input and result |
| --- | --- | --- |
| GET | `/projects` | Registered project definitions. |
| POST | `/projects` | name, optional preset/existingPath; 201 and the created project. |
| DELETE | `/projects/{projectId}` | Remove server registration; 204. |
| GET | `/projects/{projectId}/git/status` | Git status, or 204 if unavailable. |
| GET | `/projects/{projectId}/git/history` | Recent revisions. |
| POST | `/projects/{projectId}/git/exchange` | Signed exchange result. |
| GET | `/projects/{projectId}/backups` | Backup entries. |
| POST | `/projects/{projectId}/backups` | Create a backup and return its result. |
| POST | `/projects/{projectId}/sync/configure` | executablePath; dedicated runtime configuration. |
| POST | `/projects/{projectId}/sync/start` | Start service; current state. |
| POST | `/projects/{projectId}/sync/pause` | Pause service; current state. |
| POST | `/projects/{projectId}/sync/stop` | Stop service; current state. |
| GET | `/projects/{projectId}/sync/status` | Synchronization runtime state. |
| POST | `/projects/{projectId}/peers/invitations` | Optional role; exchangeText, verificationCode and expiry. |
| POST | `/projects/{projectId}/peers/join-request` | exchangeText and verificationCode; signed response text. |
| POST | `/projects/{projectId}/peers/approve` | exchangeText; approved membership text. |
| POST | `/projects/{projectId}/peers/membership` | exchangeText; import the signed grant. |
| GET | `/projects/{projectId}/peers` | Authorized members. |
| DELETE | `/projects/{projectId}/peers/{deviceId}` | Revoke the selected device; 204. |
| POST | `/projects/{projectId}/vpn/configure` | Optional networkCidr, localAddress, listenPort, publicEndpoint, capabilities; profile. |
| GET | `/projects/{projectId}/vpn/profile` | VPN profile. |
| GET | `/projects/{projectId}/vpn/status` | Tunnel runtime state. |
| POST | `/projects/{projectId}/vpn/start` | Explicit tunnel start. |
| POST | `/projects/{projectId}/vpn/stop` | Explicit tunnel stop. |
| POST | `/projects/{projectId}/vpn/invitations` | Optional capabilities; signed exchangeText and expiry. |
| POST | `/projects/{projectId}/vpn/join` | exchangeText, optional capabilities; signed response. |
| POST | `/projects/{projectId}/vpn/accept` | exchangeText; accept signed response. |
| DELETE | `/projects/{projectId}/vpn/peers/{peerId}` | Remove the VPN peer; 204. |
| GET | `/projects/{projectId}/chat/snapshot` | Optional user/since query; chat snapshot. |
| POST | `/projects/{projectId}/chat/messages` | Message request; sent-message result. |
| POST | `/projects/{projectId}/chat/channels` | Channel request; created-channel result. |
| GET | `/projects/{projectId}/chat/messages/{messageId}/attachment` | Attachment payload serialized as JSON. |

## Usage {#example}

Use the presets returned by capabilities instead of assuming every desktop mode is supported. A successful HTTP response does not implicitly authorize a peer: follow invitation, verification and membership. VPN does not replace Git/Sync permissions.

Replace the token placeholder only in your local client. Do not save it in a shared example, URL or public profile.

[Configure the server](doc:server-setup)
