---
id: code-and-ai
title: Code explorer, global search, and AI permissions
description: Browse a complete project tree, search with Ctrl+Shift+F, follow selected-line history, and connect an optional AI agent safely.
section: Workflows / Code and assets
parent: code-explorer
order: 24
revision: 2
---

## Code explorer {#code-explorer}
The Code tab shows a hierarchical project tree, file metadata, text previews, and detected symbols. Build outputs, Unreal caches, .git, and dependency caches are excluded automatically. Hidden files remain optional.

## Global search {#global-search}
Press Ctrl+Shift+F from anywhere in CyRevision. Search supports case, whole words, regular expressions, and semicolon-separated patterns such as *.cs;*.cpp. ripgrep is used when available, with a managed fallback.

## Selection history {#selection-history}
Select lines in the preview and choose History of selection. CyRevision follows that inclusive line range through Git. Selecting a folder loads history for the complete subtree.

## Optional ai {#optional-ai}
Enable AI Workspace from Plugins. Connect Codex CLI, OpenAI Responses API, a compatible API, Ollama, or LM Studio. API keys stay in memory for the current run only.

## Permissions {#permissions}
Read is required. File modification, network access, staging, and commit are separate choices. Codex uses a read-only sandbox unless edits are explicitly allowed. CyRevision brokers stage and commit after a successful run. Push is never automatic.

## OpenCode and provider configuration {#opencode-and-provider-configuration}
The AI provider selector also supports OpenCode CLI. Choose OpenCode, set its executable (opencode or an explicit installed location) and the model/provider configuration expected by your CLI. Codex CLI is a different provider; changing the selector should not keep the wrong executable. APIs and local-model endpoints require their own compatible configuration.

## Review a turn {#review-a-turn}
Choose project context and capabilities, send the prompt, observe the running state and cancel if needed. Review generated diffs and run tests before applying/staging/committing. An isolated worktree is another checkout, not permission to publish. Read the selected provider's status: this guide does not promise identical sandbox enforcement for every external CLI.

```image
{"id":"ai","src":"assets/ai.en.svg","alt":"Code explorer, global search, and AI permissions","controls":"minimal"}
```

[See also](doc:mcp-policy)
