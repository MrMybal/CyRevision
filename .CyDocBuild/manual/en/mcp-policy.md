---
id: mcp-policy
title: MCP servers and emergency blocking
description: Configure local or remote MCP servers per project and restrict every server or tool.
section: Workflows / Code and assets
parent: code-and-ai
order: 25
revision: 2
---

## Transports {#transports}
The MCP tab supports STDIO processes and Streamable HTTP addresses. Configure commands, arguments, working directories, environment forwarding, URLs, OAuth, bearer-token environment variables, and HTTP headers. STDIO is available to local Codex providers. Responses API providers use remote HTTP MCP only, require network permission and an explicit tool allow list, and resolve authorization from the configured environment variable.

## Tool policy {#tool-policy}
Each server has an enabled switch, read-only or read/write capability, network requirement, startup/tool timeout, allow list, deny list, default approval mode, and per-tool approval overrides. The deny list wins after the allow list. API approval requests are never accepted silently; only an explicit Approve policy permits immediate execution.

## Project isolation {#project-isolation}
Profiles are stored in local CyRevision configuration, outside Git. Block unmanaged Codex servers exposes only the servers listed for the current project. ReadWrite servers require workspace modification permission; HTTP and network servers require network permission.

## Emergency block {#emergency-block}
BLOCK ALL MCP persists an all-server block and cancels the current CyRevision AI run. Unblock removes the emergency state but keeps individual blocks and permissions. MCP is disabled by default. Prefer environment-variable references for secrets.

```image
{"id":"ai","src":"assets/ai.en.svg","alt":"MCP servers and emergency blocking","controls":"minimal"}
```

[See also](doc:code-and-ai)
