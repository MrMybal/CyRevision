---
id: members-security
title: Members, invitations and roles
description: Separate repository, synchronization and VPN authorization.
section: Workflows / Collaboration
parent: sync-and-peers
order: 30
revision: 2
---

## Location {#location}
Open Overview > Members. Inspect Git/Sync/VPN participants and authorized peer membership separately. A hosting-service collaborator, a synchronized device and a VPN peer are not the same grant.

## Invite a device {#invite-a-device}
1. Create the supported single-use invitation.
2. Transmit its verification code through another trusted channel.
3. Check the returned device identity and requested role.
4. Approve explicitly and verify the resulting membership.

Never approve solely because a device name resembles a teammate's. Roles determine allowed project operations; use the minimum required capability. A reachable VPN address alone does not authorize repository or shared-folder access.

## Change or revoke {#change-or-revoke}
Review the target device and consequences before changing roles, rotating credentials or revoking membership. Revocation stops future authorized operations; it cannot retrieve copies already delivered to another device. Coordinate device replacement and recovery with the project owner.

If a peer is missing, inspect selected project, invitation expiry, approval state and transport reachability separately. Do not solve membership failures by sharing private peer configuration files.

```image
{"id":"sync","src":"assets/sync.en.svg","alt":"Members, invitations and roles","controls":"minimal"}
```

[See also](doc:vpn)
