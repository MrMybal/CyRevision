---
id: server-setup
title: Optional server peer
description: Connect an always-available peer without making local Git server-dependent.
section: Workflows / Collaboration
parent: sync-and-peers
order: 145
revision: 1
---

## When to use it {#when-to-use-it}

CyRevision Server is an optional always-available peer with backups and a web dashboard. Desktop Git remains usable without it. Starting a server does not authorize it for a project.

## Prepare a deployment {#deployment}

Use the supplied deploy/docker-compose.yml and its example environment file. Choose a dedicated data volume and set CYREVISION_SERVER_TOKEN in the deployment's local environment. The dashboard uses port 8080; put an HTTPS reverse proxy in front of it on untrusted networks.

The regular deployment grants no network-administration privilege. Add the VPN compose overlay only if this server must run WireGuard. Git, Sync and Backup do not require that overlay. The supplied systemd unit binds to loopback by default and uses an administrator-managed environment file.

## Connect and verify {#connect-and-verify}

1. Check /health and /api/v1/capabilities.
2. Open the dashboard and enter the API token for that browser session.
3. Register the intended project and its server-side location.
4. Configure and start only the needed Sync or VPN services.
5. Inspect the final project and service states before exchanging files.

Without a configured token the server creates config/server-token.txt under its data directory. Read it locally as the service owner; never expose it through a public share.

## Retention and recovery {#retention}

Choose backup storage explicitly. Current synchronized files are not a backup. Keep retention and recovery copies independent, and verify a restore into another folder before removing originals.

[HTTP API](doc:server-api) · [Membership](doc:members-security)
