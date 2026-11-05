---
id: discord-agent
title: Discord project agent
description: Publish grouped Git updates to one Discord channel without requiring GitHub.
section: Integrations
order: 35
revision: 1
---

## Channel webhook {#channel-webhook}
Create an incoming webhook in the Discord channel that should receive notifications, then paste its URL in the Discord agent tab. A webhook belongs to a specific channel, so CyRevision does not need a global bot token.

## Execution modes {#execution-modes}
Integrated runs inside the desktop application. Autonomous runs as a multi-project sidecar and continues when the desktop closes. Launch local agent starts the packaged sidecar; a remote server or VPN peer can run the same executable. The control API is Bearer-authenticated, uses loopback by default, requires HTTPS remotely, and allows private HTTP only after explicit trusted-WireGuard approval.

## Safe baseline {#safe-baseline}
The first start records the current HEAD and does not publish old history. Later checks group new commits into one message and can also report active-branch changes. Pulls, peer exchanges, and local commits are detected once they become visible in the local Git repository.

## Duplicate prevention {#duplicate-prevention}
CyRevision stores a per-project checkpoint only after Discord accepts a message. Run automatic monitoring on a single agent per project; two active agents have independent local checkpoints and may both report the same update.

## Secrets and mentions {#secrets-and-mentions}
The webhook and autonomous control token remain in local user configuration, outside the project and repository. The API never returns the saved webhook. Automatic messages disable Discord mentions.

## Limits {#limits}
The agent reports Git history visible locally and works without GitHub. Pull requests, issues, and review events belong to a forge and require that forge's own Discord integration.

```image
{"id": "integrations", "src": "assets/integrations.en.svg", "alt": "Workflow overview", "controls": "minimal"}
```

[See also](doc:members-security)
