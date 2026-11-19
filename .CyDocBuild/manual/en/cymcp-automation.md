---
id: cymcp-automation
title: CyMCPSDK assistant, CLI and MCP
description: Configure the integrated assistant, automate real project operations, and connect external clients using the shared permission-controlled registry.
section: Workflows / Code and assets
parent: code-and-ai
order: 25
revision: 2
---

## Architecture {#architecture}
CyRevision compiles CyMCPSDK 0.7.0 (development revision `791d4d7d7680213b282ec8529d248a10f8494d77`) from the source snapshot pinned in `native/cymcp.lock.json`. Core, Platform, Http, MCP and AI are linked into the native bridge. Local is not needed: existing Ollama and LM Studio endpoints can be used without embedding a model runtime. ChatUI requires newer CyUI composer/transcript APIs than the application's pinned CyUI; the existing Avalonia and ImGui interfaces therefore render SDK Conversation events. Existing assistants, MCP profiles and launch options remain available. CyToolsCore was not present.

One project-bound registry calls the actual application services. Chat, the console companion and external MCP share its validation, permissions and handlers. Headless operations cover Git/LFS, files, code search and backups. The open desktop also exposes its reviewed workflows through `app.*`: PR/CI, synchronization, VPN/Swarm, members, files/chat, builds, plugins, engine integrations and preferences. These live bindings use the same view-model actions and preference service as the GUI, not a separate simulated implementation. Live operations require `--instance`; they are unavailable in a headless process.

## Assistant setup {#assistant}
Enable AI Workspace in Plugins, then open Code & Assets > AI Assistant > CyMCPSDK. List providers to obtain the exact provider IDs, detect installed CLIs/local services, and list models before selecting an exact model ID. Discovery is not proof that an account has authenticated successfully.

Configure provider ID, model, optional endpoint/executable, and the **name** of an API-key environment variable. Connect does not send a prompt. Send streams text into the transcript; Cancel turn stops generation and pending confirmations. File attachments are explicit local selections embedded by the SDK, not permission to browse the filesystem. SDK limits apply (4 MiB per attachment, 8 MiB per message).

API/local providers and Codex can receive registry tools directly. Other CLI agents need a supported native MCP configuration: start local MCP first. Unsupported CLI providers are not silently given tools. CLI agents keep native context management; the API context budget is not a CLI compaction setting.

History IDs identify SDK sessions in the private project-specific automation data directory, independently of legacy conversation files. Save session checkpoints the complete conversation; Restore saved session loads it on connection. Existing history is not overwritten by a new connection unless it is restored. Disconnect before changing provider/session settings. Restart SDK runtime applies optimization changes and disconnects old providers and external servers.

## Permissions and operations {#operations}
Read is enabled by default. File changes, staging, commit and network access are independent grants, initially disabled. Separate opt-in grants control UI navigation/selection, publication/sending, destructive operations, service control, project/application administration and builds/agent execution. Every mutation requires approval of its exact arguments. Navigation, selection and task searches also require confirmation. The application-wide confirmation window is usable from any tab during generation. File writes require the SHA-256 from `file.read`, or `missing` for a new destination; existing contents are copied to recovery storage before replacement. Conflicting/stale writes fail. Publication permission alone never approves a push or PR merge.

The selected desktop project's modes and enabled plugins are enforced. Switching projects cancels the old turn, denies the old registry access and resets every grant. Reconnect to the new project scope before further calls. The existing MCP emergency block applies to this registry too. Server-supplied read-only annotations do not grant application permissions. Revocation is checked again after confirmation, before the handler starts.

In Git + Sync, the existing GUI commit workflow may also exchange revisions: its live automation action therefore requires network, publication and service grants in addition to commit/stage/write. Changing files already watched by an independently running synchronization service can have the service's usual effects; permission to write is not a promise to disable external watchers.

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

The live registry now includes PR creation/review/comment/merge, CI dispatch/cancellation/rerun, conflict workflows, Sync/VPN/Swarm control, peer-role management, project/plugin configuration and builds, subject to their distinct grants and existing application checks. `app.preferences.*` uses Edit > Preferences validation and callbacks. `app.appearance.*` controls the native frontend's theme, density and language; read its acknowledgement to distinguish queued from applied changes. Backup restore through the typed file operation still only targets absent files.

Assistant grants/approvals, account credentials, private invitations, identity/certificate enrollment, token rotation, executable installation/path trust, application replacement and arbitrary shell execution remain human-owned. No tool can enable its own authority. `app.capabilities` lists reviewed actions and exclusions; new application methods do not acquire model access automatically. Cancellation is forwarded to the owning operation, but cannot undo an external request already accepted by a server.

### Operating the live application {#live-application}

1. Read `app.capabilities` and the exact operation schema. Check mode/plugin availability and required grants.
2. Read `app.context` with the workspace ID, such as `pullrequests`, `ci` or `plugins`. Wait while `busy` is true.
3. Use `app.table.read` to obtain stable keys, then `app.table.select` or `app.table.setFlag` to prepare the real selection. Use the named `app.fields.set.*` tools for editable drafts or enum options. Changes do not implicitly save or publish.
4. Read a fresh context after every edit/selection. Pass its exact `stateToken` and `target` to `app.<workspace>.<ActionName>`. Approval is single-use; if the selection, draft or scope changed, `StaleState` requires a new context and confirmation.
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

Use `--arguments-file` to avoid shell-dependent JSON quoting. `--profile-dir` selects automation data; `--backup-store` selects the actual backup store. Direct mutations need relevant `--allow-write`, `--allow-stage`, `--allow-commit`, `--allow-network` flags and `--confirm`. Extra grants are `--allow-navigation`, `--allow-publication`, `--allow-destructive`, `--allow-services`, `--allow-administration`, `--allow-builds`. For example, headless push requires write + network + publication + confirmation; pull also requires commit permission because it can create a merge commit. For chat or a server, `--approval-file` contains one exact, single-use `{ "operation": "file.write", "arguments": { ... } }` grant; `--confirm` cannot approve an entire chat/server. Attached calls always use the running instance's own grants and approval UI; CLI flags cannot override them.

Output is JSON; diagnostics go to stderr. `--stream` produces JSONL events followed by a final JSON result. Exit codes: 0 success, 2 arguments, 3 denied, 4 confirmation required, 5 missing native runtime, 6 business/protocol error, 130 cancelled. `--timeout-ms` bounds a CLI operation. Run `providers`, `discover` or `chat --provider ID --model ID --text TEXT`; use `--endpoint`, `--api-key-env`, `--executable`, `--attachment`, `--history-id`, `--restore-history` as needed. Without an explicit history ID chat creates a new one.

## MCP connections {#mcp}
Headless STDIO: configure the console companion executable with arguments `mcp --project PROJECT_FOLDER --profile-dir PRIVATE_FOLDER`. JSON-RPC alone uses stdout; it does not open a GUI. Standard MCP initialize/tool discovery/call/resource operations use CyMCPSDK's implementation.

Live instance: click Start local MCP. The displayed instance-file path contains a private bearer token and the authenticated loopback URL. An external MCP client connects to that URL plus `/mcp`, with Authorization: Bearer followed by the token read locally. Never paste the token into a tracked configuration. The health endpoint exposes only SDK liveness; business endpoints require authentication. Click Stop local MCP to stop it and remove its descriptor.

```powershell
./artifacts/native-v2/Cli/CyRevision.Cli.exe list --instance PRIVATE_INSTANCE_FILE
./artifacts/native-v2/Cli/CyRevision.Cli.exe invoke app.state --instance PRIVATE_INSTANCE_FILE
./artifacts/native-v2/Cli/CyRevision.Cli.exe serve --project . --endpoint-file PRIVATE_INSTANCE_FILE
```

The last command starts a headless loopback instance, not the open GUI. Keep it running and stop it with Ctrl+C. A forced process termination may leave a stale descriptor; a stale descriptor never restarts an operation. The SDK transports support STDIO and local authenticated HTTP/MCP, with finite SSE/subscription behavior; do not assume every third-party client supports notifications or resumable streams. TTL and explicit refresh remain available.

## External tools {#remote-tools}
Reuse the selected profile in the existing MCP tab. Explicitly enable project MCP, the server, external tools and applicable write/network permissions. Enter exact allowed tool names; the deny list wins. Connect selected MCP profile discovers only that whitelist and registers stable qualified `remote_…` names. Every forwarded call requires host confirmation, including reads; this is stricter than legacy automatic approval modes. Disabling a profile/tool revokes forwarding. Restart the SDK to change a connected server.

STDIO arguments are one per line, with configured working directory and environment overrides/forwarded names. HTTP uses a bearer-token environment name. Configured startup/tool timeouts bound SDK requests. Custom HTTP headers and OAuth/ChatGPT authentication are not implemented in this adapter: the original integrations remain available, and unsupported authentication/headers are rejected. Every forwarded call asks for approval instead of using legacy automatic approval overrides. An external error is returned as a tool error to the assistant, not a successful result.

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
