## 0.7.1 — host integration

Optional checked host schema validators preserve full application contracts. Native HTTP can derive opaque principals with a host Bearer authenticator and an explicit request bound. MCP can project legacy result text while keeping structuredContent unchanged. Default policies and schema validation remain fail-closed.

# CyMCPSDK version notes

[Français](VERSION.fr.md) · [Manual](Documentation/Documentation.html#/en/overview)

## 0.7.0 — In development

- Add opt-in private disk spooling with streaming producers, byte quotas including concurrent
  writes, UTF-8/binary range reads, cancellation and automatic temporary-file cleanup.
- Add an optional bounded performance monitor for SDK operations, MCP client/server and
  conversations. Private asynchronous JSONL records contain metadata only, with aggregate timings.
- Add optional MCP 2026 change subscriptions over HTTP SSE and stdio. Resource and tool changes
  invalidate client caches, including resource deletion. Legacy peers retain TTL behavior.
- Add optional API context windows with complete tool-turn groups, pinned system instructions
  and a host-provided incremental summarizer. Full history/checkpoints remain intact; native
  CLI agents keep their own context management.
- Chat Lab accepts independent storage, performance, notifications and context settings.
  See the [runtime options guide](Documentation/Documentation.html#/en/runtime-options).

## 0.6.0

- Add optional on-demand tool discovery without removing access to any application command.
  API/Codex registries above 32 tools use it automatically; full definitions remain selectable.
- Cache immutable operation descriptions and positive-TTL MCP discovery/resource reads.
  MCP clients support individual schema lookup, paged catalogues and explicit invalidation.
- Add native MCP resources, UTF-8/binary chunks and private, expiring large-result handles.
  Original tools retain their original output schemas and behavior.
- Add streaming literal grep over application resources and an optional ripgrep adapter
  with regex, glob filters, ignored-file opt-in, paged matches and bounded file reads.
- Chat Lab can enable compact tools and a read-only workspace in its configuration.
  See the [optimization and search guide](Documentation/Documentation.html#/en/optimization).
- Validation runs locally. The latest downloadable Chat Lab ZIP remains 0.5.6.

## 0.5.6

- Fix the fifteen issues recorded in the conformance audit and add regression coverage.
  All twelve Windows suites and all eight Linux suites pass. [Fixes and verification (French)](Docs/Audit-0.5.6.md)
- Add the resumable `codex-exec` profile and private NVIDIA CUDART 12.8.90 installation on Windows and Linux x64.
- Use the updated native CyUI message composer: growing input, formatting, Enter/Shift+Enter, and retained drafts during generation.
- Render conversations through TranscriptSource with bounded visible-row caches, without copying the full transcript on each streamed delta.
- Show native attachment cards, cached asynchronous image previews and local file previews; save embedded attachments after choosing a destination.
- Wire copy/reply actions. Hosts can opt into inline editing and deletion through explicit provider-compatible callbacks.

## 0.5.5

- Chat, Providers and Logs have dedicated tabs. Provider settings use a searchable catalogue,
  explicit connection states and a separate editor; switching providers retains their fields in memory.
- Fix automatic startup when the initial offline demo placeholder prevented an installed AI
  from being selected. Explicit provider preferences still do not fall back to another provider.
- Add timestamped diagnostics for discovery, native protocol/account checks, sessions,
  model catalogues, response start, completion, failures and cancellation.
- The Logs tab filters and copies events and opens a private session log written in the background.
  Diagnostic records omit message bodies, API keys and native session IDs.
- Remote API model selection is presented as a setup step with its returned catalogue.

## 0.5.4

- Agent-oriented defaults without arbitrary limits: model-maximum output, unlimited tool
  rounds, no turn deadline (CLI and API); `timeout_ms` only detects a silent connection.
  Remaining sizes are 256 MiB memory guards.
- Every profile publishes its conventional settings in `describe()` (`defaults`); Chat Lab
  pre-fills them in manual mode together with the detected installation.
- Discovery: npm Codex launched through its native binary, parallel probes, installed-but-stopped
  local services reported, `OLLAMA_HOST` and `ANTHROPIC_BASE_URL` normalised, llama.cpp on port
  8080. Automatic model selection is limited to local services.
- Fixes: an interrupted tool call is completed in history; a minimised UI no longer fails a turn;
  ACP resets after any mid-turn error; exact connected state after a failed restore; MCP HTTP
  legacy fallback; truncated OpenAI answers kept; MCP/settings secrets kept off the command line;
  messages too long for the Windows command line reported explicitly.
- Chat Lab offers **Sign in** to open the selected native provider's own login in a console.

## 0.5.3

- Chat Lab starts in automatic mode: installed native CLIs, supported npm installations,
  environment API keys and running local model services are detected in the background.
- Available connections open without executable paths, launch arguments or manually typed model IDs.
  Native providers retain their defaults; API text models come from their actual catalogues.
- Missing installations, keys, endpoints and local models have distinct setup states.
  Detection does not submit messages, install runtimes or read private credential files.
- Discovery is part of the AI SDK and accepts host-defined provider recipes and search locations.
  Manual connection settings remain available.

## 0.5.2

- English remains the application's default language. The local Markdown demonstration
  now follows the selected English or French language too.
- Chat Lab includes a **Help (F1)** action that opens its local manual in the selected language.
- A bilingual CyDoc manual ships with the application and can be opened directly from disk.
- The README follows the same English/French structure, with module, build and license information.

### Known issues

- The core test declaration now builds with GCC 15.2. The macOS conversation store recognizes
  the system aliases for temporary directories. Validation on a macOS runner is still pending.
  The graphical example uses Win32/DX11.
- Provider availability depends on the installed CLI/API version, account and selected model.
  The included provider profiles do not guarantee authentication with every service.

### Current limits

- The SDK does not include models, provider CLIs, inference engines, GPU runtimes or drivers.
- Local dependencies support verified archive members and a pinned NVIDIA CUDART 12.8.90
  package on Windows/Linux x64. Additional GPU packages and HTTP Range resumption remain open.
- Direct Chat Lab operation attachment is available for Codex and supported API transports;
  other CLIs use their native MCP settings. ACP filesystem/terminal services require host callbacks.
- MCP tools support stdio and authenticated loopback HTTP JSON. Persistent subscriptions,
  resources/prompts and progress notifications are not advertised.
- Android is deferred. C++ source compatibility is evolving; no cross-compiler ABI is promised.

Report reproducible problems through the [issue tracker](https://github.com/MrMybal/CyMCPSDK/issues).
No public binary release has been published for this version; build from source.

## Earlier versions

- **0.5.1:** bidirectional Chat Lab commands over MCP/SDK, Windows HTTP streaming fixes,
  corrected thinking-text display accounting and preserved failed-turn timings.
- **0.5.0:** optional CyUI chat module and Windows Chat Lab example.
- **0.4.1:** native Claude settings preserved and blocking login precheck removed.
- **0.4.0:** extensible provider profiles, conversations, attachments, private history and exact resume.
- **0.3.0:** native modular SDK foundation.
