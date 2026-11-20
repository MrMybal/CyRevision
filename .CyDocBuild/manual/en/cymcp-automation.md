---
id: cymcp-automation
title: CyMCPSDK assistant, CLI and MCP
description: Configure the integrated assistant, automate real project operations, and connect external clients using the shared permission-controlled registry.
section: Workflows / Code and assets
parent: code-and-ai
order: 25
revision: 3
---

## Architecture {#architecture}
CyRevision compiles CyMCPSDK 0.7.1 (development revision `34d4bcae126bc83d1a2925b5047dac36e3d09d92`) from the source snapshot pinned in `native/cymcp.lock.json`. Core, Platform, Http, MCP and AI are linked into the native bridge. Local is not needed: existing Ollama and LM Studio endpoints can be used without embedding a model runtime. ChatUI requires newer CyUI composer/transcript APIs than the application's pinned CyUI; the existing Avalonia and ImGui interfaces therefore render SDK Conversation events. Existing assistants, MCP profiles and launch options remain available. CyToolsCore was not present.

One project-bound registry calls the actual application services. Chat, the console companion and external MCP share its validation, permissions and handlers. Headless operations cover Git/LFS, files, code search and backups. The open desktop also exposes its reviewed workflows through `app.*`: PR/CI, synchronization, VPN/Swarm, members, files/chat, builds, plugins, engine integrations and preferences. These live bindings use the same view-model actions and preference service as the GUI, not a separate simulated implementation. Live operations require `--instance`; they are unavailable in a headless process.

## Assistant setup {#assistant}
Enable AI Workspace in Plugins, then open Code & Assets > AI Assistant > CyMCPSDK. List providers to obtain the exact provider IDs, detect installed CLIs/local services, and list models before selecting an exact model ID. Discovery is not proof that an account has authenticated successfully.

Configure provider ID, model, optional endpoint/executable, and the **name** of an API-key environment variable. Connect does not send a prompt. Send streams text into the transcript; Cancel turn stops generation and pending confirmations. File attachments are explicit local selections embedded by the SDK, not permission to browse the filesystem. SDK limits apply (4 MiB per attachment, 8 MiB per message).

API/local providers and Codex can receive registry tools directly. Other CLI agents need a supported native MCP configuration: start local MCP first. Unsupported CLI providers are not silently given tools. CLI agents keep native context management; the API context budget is not a CLI compaction setting.

History IDs identify SDK sessions in the private project-specific automation data directory, independently of legacy conversation files. Save session checkpoints the complete conversation; Restore saved session loads it on connection. Existing history is not overwritten by a new connection unless it is restored. Disconnect before changing provider/session settings. Restart SDK runtime applies optimization changes and disconnects old providers and external servers.

## Permissions and operations {#operations}
Choose **Local MCP access profile** in the CyMCPSDK settings. **Free is the default**, including migration of settings with no explicit choice. An existing explicit choice is preserved. The choice is saved per desktop project; CLI choices are saved per project under its selected profile directory.

| Profile | HTTP token | Operation grants and exact confirmation |
| --- | --- | --- |
| Free | No | No: all exposed operations are callable |
| Local controlled | No | Required |
| Token only | Required | No: all exposed operations are callable |
| Protected | Required | Required |

Free allows other local processes to read or modify the selected project and call exposed application workflows with its loopback URL alone. Enable local MCP deliberately. The profile also governs the shared registry used by CLI and chat; it does not grant a provider's own shell or operating-system permissions. After changing a running profile, old operations are blocked until **Restart SDK runtime** or **Start local MCP** applies it; reconnect clients to the new URL.

In Local controlled and Protected, file changes, staging, commit and network access are independent grants, initially disabled. Separate grants control navigation, publication, destructive operations, services, administration and builds. Mutations, navigation, selection and task searches require exact confirmation. The global approval window works during generation. Free and Token only bypass these grants and approvals, not validation. All profiles retain file SHA-256 checks (`missing` for a new file), recovery copies and rejection of stale/conflicting writes.

The selected desktop project's modes and enabled plugins are enforced. Switching projects cancels the old turn, denies the old registry access and resets every grant. Reconnect to the new project scope before further calls. The existing MCP emergency block applies to this registry too. Server-supplied read-only annotations do not grant application permissions. Revocation is checked again after confirmation, before the handler starts.

In Git + Sync, the GUI commit workflow may also exchange revisions: controlled profiles require network, publication and service grants in addition to commit/stage/write. Free and Token only allow that exposed workflow without an extra approval. Independently running synchronization watchers keep their usual effects.

| Family | Exposed operations |
| --- | --- |
| Project | `project.info`, `project.refresh` |
| Git inspection | `git.tools`, `git.status`, `git.history`, `git.fileHistory`, `git.branches`, `git.remotes`, `git.diff`, `git.commitDetails`, `git.compare`, `git.conflicts`, `git.mergeAnalysis`, `git.graph`, `git.worktrees` |
| Git/LFS changes | `git.stage`, `git.unstage`, `git.commit`, `git.branch.create`, `git.remote.set`, `git.fetch`, `git.pull`, `git.push`, `git.checkout`, `git.merge`, `git.lfs.track`, `git.lfs.untrack` |
| Git recovery/cleanup | `git.branch.analyzeRemoval`, `git.branch.remove`, `git.remote.remove`, `git.conflict.resolve`, `git.conflict.continue`, `git.conflict.abort` |
| LFS inspection | `git.lfs.patterns`, `git.lfs.files`, `git.lfs.history` |
| Files/code | `files.list`, `file.read`, `file.preview`, `file.write`, `code.search` |
| Backup | `backup.list`, `backup.create`, `backup.manifest`, `backup.restoreFile` |
| Retained results | `resources.read`, `resources.grep` |

Typed file/Git paths are relative to the host-selected project. Traversal, links, repository internals and common credential paths are excluded. Application administration is broader: existing cache, backup-store, shared-folder and service settings may refer outside the repository. Review those exact paths and effects before approval. Choose the project and provider deliberately; repository documents, history and CI logs can contain information supplied by their authors.

The live registry now includes PR creation/review/comment/merge, CI dispatch/cancellation/rerun, conflict workflows, Sync/VPN/Swarm control, peer-role management, project/plugin configuration and builds, subject to the chosen access profile and existing application checks. `app.preferences.*` uses Edit > Preferences validation and callbacks. `app.appearance.*` controls the native frontend's theme, density and language; read its acknowledgement to distinguish queued from applied changes. Backup restore through the typed file operation still only targets absent files.

Assistant grants/approvals, account credentials, private invitations, identity/certificate enrollment, token rotation, executable installation/path trust, application replacement and arbitrary shell execution remain human-owned. No tool can enable its own authority. `app.capabilities` lists reviewed actions and exclusions; new application methods do not acquire model access automatically. Cancellation is forwarded to the owning operation, but cannot undo an external request already accepted by a server.

### Operating the live application {#live-application}

1. Read `app.capabilities` and the exact operation schema. Check mode/plugin availability and required grants.
2. Read `app.context` with the workspace ID, such as `pullrequests`, `ci` or `plugins`. Wait while `busy` is true.
3. Use `app.table.read` to obtain stable keys, then `app.table.select` or `app.table.setFlag` to prepare the real selection. Use the named `app.fields.set.*` tools for editable drafts or enum options. Changes do not implicitly save or publish.
4. Read a fresh context after every edit/selection. Pass its exact `stateToken` and `target` to `app.<workspace>.<ActionName>`. `StaleState` always requires a new context; controlled profiles also require a new exact confirmation.
5. Read the updated context/table for results. Failed/cancelled business operations return errors rather than a successful completion message.

`app.tasks.providers` lists enabled task connectors. `app.tasks.search` uses their already configured project connection; results appear in `NativeWorkItemSearch`. `app.tasks.link` inserts exact result keys into the PR or commit draft: use fresh `pullrequests` context for a PR destination, or `changes` context for a commit destination. Existing linked-task completion after merge remains a separate publication action. Connection settings and tokens cannot be supplied by the model.

## CLI {#cli}
The packaged console companion is `Cli/CyRevision.Cli.exe` on Windows (`Cli/CyRevision.Cli` on Linux/macOS). Existing GUI and agent executables keep their original options. Examples from the repository root:

```powershell
./artifacts/native-v2/Cli/CyRevision.Cli.exe --help
./artifacts/native-v2/Cli/CyRevision.Cli.exe list --project .
./artifacts/native-v2/Cli/CyRevision.Cli.exe schema file.write --project .
./artifacts/native-v2/Cli/CyRevision.Cli.exe invoke project.info --project .
./artifacts/native-v2/Cli/CyRevision.Cli.exe invoke file.read --project . --arguments '{"path":"README.md"}'
```

The CLI defaults to Free. Use `--access-profile free|local-controlled|token-only|protected` to save an explicit choice. Use `--arguments-file` to avoid shell-dependent JSON quoting. `--profile-dir` selects automation data; `--backup-store` selects the actual backup store. With `--access-profile local-controlled` or `--access-profile protected`, direct mutations need relevant `--allow-write`, `--allow-stage`, `--allow-commit`, `--allow-network` flags and `--confirm`. Extra grants are `--allow-navigation`, `--allow-publication`, `--allow-destructive`, `--allow-services`, `--allow-administration`, `--allow-builds`. For example, headless push requires write + network + publication + confirmation; pull also requires commit permission because it can create a merge commit. For chat or a server, `--approval-file` contains one exact, single-use `{ "operation": "file.write", "arguments": { ... } }` grant; `--confirm` cannot approve an entire chat/server. Attached calls use the running instance's profile; CLI flags cannot override it. With token profiles, supply its private descriptor or `--instance URL --token TOKEN`.

Output is JSON; diagnostics go to stderr. `--stream` produces JSONL events followed by a final JSON result. Exit codes: 0 success, 2 arguments, 3 denied, 4 confirmation required, 5 missing native runtime, 6 business/protocol error, 130 cancelled. `--timeout-ms` bounds a CLI operation. Run `providers`, `discover` or `chat --provider ID --model ID --text TEXT`; use `--endpoint`, `--api-key-env`, `--executable`, `--attachment`, `--history-id`, `--restore-history` as needed. Without an explicit history ID chat creates a new one.

## MCP connections {#mcp}
Headless STDIO: configure the console companion executable with arguments `mcp --project PROJECT_FOLDER --profile-dir PRIVATE_FOLDER`. JSON-RPC alone uses stdout; it does not open a GUI. Standard MCP initialize/tool discovery/call/resource operations use CyMCPSDK's implementation.

Live instance: click Start local MCP and copy the displayed `http://127.0.0.1:PORT/mcp` address. In Free, that address alone is enough for MCP initialization, discovery and every exposed read/write operation. Local controlled also accepts connections without a token, but checks operation grants/approvals. Token only and Protected require `Authorization: Bearer TOKEN`; read the generated token from the private instance descriptor. A missing or invalid token returns HTTP 401. Never publish that descriptor or token. The server always binds to IPv4 loopback and retains Host/Origin protections. Stop local MCP removes its descriptor and stops the endpoint.

```powershell
./artifacts/native-v2/Cli/CyRevision.Cli.exe list --instance PRIVATE_INSTANCE_FILE
./artifacts/native-v2/Cli/CyRevision.Cli.exe invoke app.state --instance PRIVATE_INSTANCE_FILE
./artifacts/native-v2/Cli/CyRevision.Cli.exe invoke app.state --instance http://127.0.0.1:PORT/mcp
./artifacts/native-v2/Cli/CyRevision.Cli.exe serve --project . --endpoint-file PRIVATE_INSTANCE_FILE
```

The last command starts a headless loopback instance, not the open GUI. Keep it running and stop it with Ctrl+C. A forced process termination may leave a stale descriptor; a stale descriptor never restarts an operation. The SDK transports support STDIO and local HTTP/MCP with profile-dependent authentication, with finite SSE/subscription behavior; do not assume every third-party client supports notifications or resumable streams. TTL and explicit refresh remain available.

## External tools {#remote-tools}
Reuse the selected external server configuration in the MCP tab. Enable project MCP and the server, and enter exact allowed tool names; the deny list wins. Connect selected MCP profile registers only that whitelist with stable `remote_…` names. Local controlled and Protected additionally require external-tool/write/network grants and confirmation for every forwarded call, including reads. Free and Token only do not ask for those extra grants or confirmations. Disabled/unconfigured servers and tools remain unavailable. Restart the SDK to replace a connection.

STDIO arguments are one per line, with configured working directory and environment overrides/forwarded names. HTTP uses a bearer-token environment name. Configured startup/tool timeouts bound SDK requests. Custom HTTP headers and OAuth/ChatGPT authentication are not implemented in this adapter: the original integrations remain available, and unsupported authentication/headers are rejected. Controlled profiles ask for approval instead of using legacy automatic approval overrides. An external error is returned as a tool error to the assistant, not a successful result.

Headless chat accepts `--remote-config-file PRIVATE_JSON_FILE`: SDK connection fields `id`, `url` or `executable`/`arguments`/`cwd`/`environment`, `allowedTools`, optional `tokenEnvironment`, `cacheTtlMs`. It is an explicit host configuration, not model-provided connection data. Grant a single exact qualified operation through `--approval-file` when needed.

## Independent optimizations {#optimizations}
Compact discovery is the default: five gateway tools search names/descriptions, describe one schema, invoke it, read resources and grep snapshots. Full discovery is selectable (`--full-discovery`). Both reach the same registry; changing discovery does not grant permissions.

Bounded read-only results also become immutable grep-able snapshots (at most 16, 1 MiB each), invalidated by mutations and explicit refresh. `code.search` uses the existing ripgrep/managed service only within the selected project. File reads have byte ranges and base64 for exact retrieval. Structured lists use offset/limit.

Oversized results return a private retained resource. Read its manifest, then follow `firstUri` and `nextUri`; never invoke the original action again to obtain the remainder. Default retained-result memory quota is 64 MiB, chunk size 32 KiB, retention 300 seconds. Disk spool is opt-in (default 1 GiB quota), under the private automation data directory. Memory/disk quotas, chunks, TTL and profile/cache location can be configured separately. These are temporary results, not durable exports; SDK conversation checkpoints and recovery copies have separate storage.

Discovery/read cache TTL defaults to 30 seconds (`0` disables it). Change notifications and metadata-only JSONL measurements are separately opt-in. `project.refresh` invalidates snapshots and external discovery/read caches; `app.refresh` also refreshes the real desktop. Mutations are not cached or replayed. API context budget defaults to 0 (unlimited); a nonzero byte budget preserves the full saved transcript, system instruction and complete active tool group, evicting only older context from API requests. Oversized active groups fail rather than dropping results. No summary is invented.

## Build and validation {#build}
Windows: `powershell -NoProfile -File scripts/build-native-v2.ps1` source-builds the pinned SDK and publishes GUI plus CLI. SDK alone: `scripts/build-cymcp.ps1`. Linux/macOS: `bash scripts/build-cymcp.sh` requires CMake 3.24+, C++20 and libcurl development files, on the target architecture; the existing release scripts include the CLI and runtime. Cross-architecture native binaries must be built on that target or supplied with `CyMcpNativeRuntimeDirectory`, never replaced by the host DLL.

Local regression commands:
```text
python scripts/verify-cymcp-snapshot.py
python scripts/test_cymcp_integration.py --cli artifacts/native-v2/Cli/CyRevision.Cli.exe
python scripts/test_cymcp_desktop.py --app artifacts/native-v2/CyRevision.Desktop.exe --cli artifacts/native-v2/Cli/CyRevision.Cli.exe
python scripts/test_cymcp_full_app.py --app artifacts/native-v2/CyRevision.Desktop.exe --cli artifacts/native-v2/Cli/CyRevision.Cli.exe
python scripts/test_cymcp_git_actions.py --cli artifacts/native-v2/Cli/CyRevision.Cli.exe
dotnet test CyRevision.sln -c Release
```
The integration fixtures use real application services, disposable Git repositories, a local bare remote and simulated local model/GitHub API servers. Tests exercise confirmed checkout, merge/conflict recovery, PR comment/merge, CI cancellation, preferences, stale targets and grant revocation. Catalogue tests classify every public UI action as reviewed or host-only. They do not certify real provider accounts, production CI, operating-system VPN/firewall changes or every external plugin installation. Windows x64 is the exercised platform; Linux/macOS native builds still need platform testing.

[Existing AI workflows](doc:code-and-ai) · [Existing MCP policy](doc:mcp-policy)
