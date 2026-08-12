# Code explorer, global search, and AI workspace

## Code workspace

The **Code** tab is a repository-aware workspace that remains available without Rider or Unreal Editor. It includes:

- a hierarchical folder and file explorer with path filtering, file metadata, language detection, and hidden-file control;
- automatic exclusion of `.git`, build output, Unreal caches, dependency caches, and reparse-point directories;
- a safe text preview limited to 2 MiB, binary detection, and a symbol outline for common C#, C/C++, Python, JavaScript, and TypeScript declarations;
- Git history for the selected file or folder;
- selected-line history backed by `git log -L`;
- project-wide text search with locations, previews, cancellation, regex, case, whole-word, and file-pattern options.

Press **Ctrl+Shift+F** anywhere in CyRevision to open this workspace and focus the global search field. Press Enter to start the search.

CyRevision uses `ripgrep` and its JSON output when `rg` is available. A managed .NET search engine is used as a fallback, so the feature is still functional on a fresh installation. Result counts are capped and large files are skipped to keep very large Unreal repositories responsive.

## Selection history

Select a file, highlight the relevant lines in the preview, then choose **History of selection**. CyRevision converts the text offsets to an inclusive line range and asks Git to follow that range through history. This is more focused than ordinary file history and is useful when a class contains several unrelated systems.

Renames and complex refactors can still limit Git's line tracking. In that case, use the complete file history as the broader fallback.

## Optional AI Workspace plugin

`CyRevision.Plugin.AI` is packaged with releases but disabled by default. Enable **AI Workspace** from **Plugins** to use the **AI Assistant** tab.

Available providers:

- installed Codex CLI;
- OpenAI Responses API;
- a configurable Responses-compatible API;
- Codex with the local Ollama provider;
- Codex with the local LM Studio provider.

API keys are session-only fields and are cleared after a run. They are not written to CyRevision configuration or the project.

## Permission broker

Every run receives an explicit permission set:

- repository read access is required;
- file modification changes Codex from `read-only` to `workspace-write`;
- network access enables web search for that run;
- stage and commit permissions authorize CyRevision's Git broker after a successful run.

The agent prompt forbids `git add`, `git commit`, `git push`, and history rewriting. When staging or committing is authorized, CyRevision inspects the resulting working tree and performs the requested Git operation itself. Push is intentionally never automatic.

API-only providers currently operate as advisory agents: they can analyze and propose changes, while workspace edits require a local Codex provider running inside the sandbox.

## MCP servers

The **MCP** tab configures Model Context Protocol servers for local Codex providers and remote HTTP MCP servers for Responses API providers. Profiles are stored in the CyRevision user configuration directory and are never written to the repository or its `.codex` directory.

CyRevision supports both transports used by Codex:

- **STDIO**: command, one argument per line, working directory, static environment values, and environment-variable forwarding;
- **Streamable HTTP**: URL, OAuth or ChatGPT authentication mode, bearer-token environment variable, OAuth scopes/resource, static headers, and headers sourced from environment variables.

Every server has independent controls for enabled/blocked state, required startup, read-only or read/write capability, network requirement, startup timeout, tool timeout, default approval mode, enabled-tool allow list, disabled-tool deny list, and per-tool approval overrides.

The deny list is applied after the allow list. Prefer environment-variable references for tokens. Static environment values and headers are supported but are stored in the local CyRevision profile.

For Responses API providers, CyRevision uses only Streamable HTTP servers, requires network permission and a non-empty tool allowlist, subtracts every denied tool from that list, and resolves authorization through the configured environment-variable name. STDIO, Codex OAuth sessions, and custom HTTP headers remain local-Codex features. Approval modes other than `Approve` are sent as `require_approval: always`; CyRevision stops on the approval request instead of silently authorizing it.

### Blocking and isolation

MCP is disabled by default for every project. **Block unmanaged Codex servers** starts each CyRevision run with an empty MCP table and adds only the servers configured in that project profile. This prevents MCP servers from a separate user-level Codex configuration from being inherited accidentally.

Servers marked **ReadWrite** are omitted unless workspace modification is authorized. HTTP servers and servers marked as requiring network are omitted unless network permission is authorized.

**BLOCK ALL MCP** sets the persistent emergency block, produces an empty MCP configuration, and cancels a CyRevision AI run already in progress. **Unblock** removes only the emergency state; individual server blocks, tool deny lists, and workspace permissions remain in force.

For non-interactive Codex runs, approval modes that require a prompt remain restrictive. Use `approve` only for tools that the project owner explicitly trusts. CyRevision still never grants MCP an automatic Git push operation.
