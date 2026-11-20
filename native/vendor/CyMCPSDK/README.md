<table width="100%">
  <tr>
    <td width="300" align="left" valign="top">
      <a href="Documentation/Documentation.html#/en/overview">Manual</a><br>
      <a href="https://github.com/MrMybal/CyMCPSDK">Repository</a><br><br>
      <sub>README</sub><br>
      <a href="README.md">English</a> · <a href="README.fr.md">Français</a>
    </td>
    <td width="400" align="center" valign="middle">
      <strong>CyMCPSDK</strong><br><sub>C++20 SDK</sub>
    </td>
    <td width="300" align="right" valign="top">
      <p><sub>Library license</sub><br><a href="LICENSE"><strong>MIT</strong></a></p>
      <p><sub>Version</sub><br><a href="VERSION.md"><strong>0.7.1</strong></a><br><strong>IN DEVELOPMENT</strong></p>
      <p><a href="https://github.com/MrMybal/CyMCPSDK/releases/tag/v0.5.6"><sub>Download Chat Lab · Windows x64</sub></a></p>
    </td>
  </tr>
</table>

<h1 align="center">CyMCPSDK</h1>

A modular C++20 library for application developers who want AI agents to use their app,
conversations inside the app, and connections to other applications' tools. It provides
native SDK/MCP transports and an optional CyUI Markdown chat panel.

> **IN DEVELOPMENT · 0.7.1**
>
> The Windows chat example is available as a portable ZIP. Linux/macOS distribution and dependency
> installation work remain in progress. Read the [version notes and known issues](VERSION.md).

[![Chat Lab provider catalogue and dedicated connection settings](assets/screenshots/providers-en.png)](assets/screenshots/providers-en.png)

## What you can build

Register your application's operations once, then expose them through native calls, SDK JSON
or an authenticated local MCP server. Connect an AI provider to those operations, or use the
MCP client to forward selected tools from another application.

Use CLI agents, native APIs or an existing local model service for conversations. The provider
registry includes 23 extensible profiles, including Codex, Claude Code, OpenCode, Antigravity,
ACP agents, Responses, Messages, OpenRouter and Ollama. Actual models and capabilities come
from the configured provider. Credentials and model weights are supplied separately.

Chat Lab demonstrates streamed messages, Markdown/code/tables, attachments, cancellation,
exact session reconnection and optional history. Its demonstration counter can be used by an
AI or another MCP/SDK client. English is the default; French and five CyUI themes are available.
Chat opens directly. **Settings** contains **Providers**, **Logs** and **Help**. Search the catalogue,
read the session log as a table or raw JSONL, or open the offline manual. **F1** opens Help and
**Close** returns to the conversation.
See the [journal of a simulated model refusal](assets/screenshots/logs-en.png) and the [earlier capture of a real streamed Codex reply](assets/screenshots/chat-auto-en.png).
Native providers offer **Sign in** when authentication is required. Manual provider selection
pre-fills conventional settings; remote APIs display their catalogue for an explicit model choice.

## Modules

| CMake target | Purpose |
| --- | --- |
| `CyMCP::Core` | Typed operations, schemas, permission policies, cancellation and workers |
| `CyMCP::Platform` | Child processes with structured arguments and owned-process cleanup |
| `CyMCP::Http` | HTTP(S), streaming and authenticated loopback JSON server |
| `CyMCP::MCP` | MCP tools over stdio and local HTTP JSON, server and client |
| `CyMCP::AI` | Provider factories, CLI/ACP/API transports, conversations and tool exchanges |
| `CyMCP::Local` | Local engine supervision and verified dependency downloads |
| `CyMCP::ChatUI` | Optional asynchronous controller and native CyUI chat panel |
| `CyMCP::SDK` | SDK aggregate without graphical dependencies |

Host policies can restrict operations or explicitly permit all registered actions. Native agent
permissions remain separate. No Python runtime, AI CLI, inference engine, model, CUDA runtime
or driver is bundled. Android is deferred.

Large tool catalogues use on-demand discovery while keeping every command accessible.
Native MCP resources, private TTL caches and chunked results reduce traffic. Native resource
grep and optional workspace ripgrep are described in the [optimization guide](Documentation/Documentation.html#/en/optimization).

---

## Installation

Download [Chat Lab 0.5.6 for Windows x64](https://github.com/MrMybal/CyMCPSDK/releases/download/v0.5.6/CyMCPSDK-ChatLab-0.5.6-windows-x64.zip).
The release contains the chat example. Build the C++ SDK from source for integration into your own app.
For Chat Lab, use Windows 10/11 x64, the Microsoft Visual C++ 2015–2022 x64 runtime and DX11
(WARP fallback available). Keep `Documentation/`, `licenses/` and notices beside the executable.

1. Extract the complete ZIP into a folder, or build the example with the commands below.
2. Run `cymcp_chat_app.exe` from the extracted folder (`out/ChatLab-windows-x64/` for a source build).
3. Keep **Automatic connection** enabled. Installed CLIs, running local services and environment API keys
   are detected; the first available connection is prepared without sending a message.
4. Click another detected connection if needed, then send with **Enter** (or **Ctrl+Enter**). Actual models appear in a dropdown.

Manual settings remain available in **Providers**. To try without an AI account,
select **Demo / Markdown (no AI)** there and choose **Connect / New chat**.

Open [Documentation/Documentation.html](Documentation/Documentation.html#/en/quickstart)
from a local checkout or package. Download the complete documentation folder; GitHub's HTML
file viewer displays source, so it is not the manual reader. Reading locally needs no server or Node.js.

## Source, build and required libraries

### Requirements

- CMake 3.24+, a C++20 compiler and system threads.
- Windows: Visual Studio 2022 C++ tools and Windows SDK.
- Linux/macOS: POSIX APIs and libcurl development files for HTTP(S); see current platform issues.
- Optional chat UI: a host [CyUI](https://github.com/MrMybal/CyUI) checkout with DocumentViews.

```sh
git clone https://github.com/MrMybal/CyMCPSDK.git
cd CyMCPSDK
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release --parallel 4
cmake --install build --config Release --prefix install
```

For the Windows chat example, place CyUI beside the SDK or pass its real path:

```powershell
./tools/build-chat.ps1 -CyUISource ../CyUI
./out/ChatLab-windows-x64/cymcp_chat_app.exe
```

<details>
<summary>Link the library and choose optional modules</summary>

```cmake
find_package(CyMCPSDK 0.5 CONFIG REQUIRED)
target_link_libraries(my_app PRIVATE CyMCP::Core CyMCP::MCP CyMCP::AI)
```

Alternatively use `add_subdirectory(external/CyMCPSDK)`. Disable unneeded modules with
`CYMCP_BUILD_HTTP`, `CYMCP_BUILD_PLATFORM`, `CYMCP_BUILD_MCP`, `CYMCP_BUILD_AI` and
`CYMCP_BUILD_LOCAL`. CMake checks dependencies. The `core-only` preset builds the operation core.
`CYMCP_BUILD_CHAT_UI` and `CYMCP_BUILD_CHAT_APP` default to off. An installed ChatUI consumer
provides `CyUI::DocumentViews` before requesting `find_package(... COMPONENTS ChatUI)`.
See the [integration manual](Documentation/Documentation.html#/en/integration).

</details>

---

## License

CyMCPSDK uses the [MIT license](LICENSE). Copyright © 2026 Cyberalien.
External engines, models and provider services retain their own terms.

### Third-party libraries

| Library · source | License · terms | Use / scope |
| --- | --- | --- |
| [nlohmann/json](https://github.com/nlohmann/json) 3.12.0 | [MIT](third_party/nlohmann/LICENSE.MIT) | SDK JSON types and validation |
| [cpp-httplib](https://github.com/yhirose/cpp-httplib) 0.66.0 | [MIT](third_party/httplib/LICENSE) | Local HTTP server; development fixtures |
| [PicoSHA2](https://github.com/okdshin/PicoSHA2) | [MIT](third_party/picosha2/LICENSE) | Artifact hashing |
| [libcurl](https://curl.se/libcurl/) | [curl license and TLS notices](THIRD_PARTY_NOTICES.md) | Host HTTP(S) dependency on Linux/macOS |
| [CyUI](https://github.com/MrMybal/CyUI), Dear ImGui, MD4C | [MIT notices](THIRD_PARTY_NOTICES.md) | Optional native UI/Markdown modules |
| Lexilla/Scintilla lexers | [Permissive notices](THIRD_PARTY_NOTICES.md) | Optional CyUI code highlighting |
| JetBrains Mono | [SIL OFL 1.1 notice](THIRD_PARTY_NOTICES.md) | Optional CyUI embedded font |
| [CyDoc](https://github.com/MrMybal/CyDoc) 0.1.0 | [MIT](Documentation/DocData/CyDoc-MIT.txt) | Static documentation reader; not a native SDK dependency |
| marked, PrismJS, esbuild | [MIT notices](THIRD_PARTY_NOTICES.md) | Documentation generation only |

Retain the complete [third-party notices](THIRD_PARTY_NOTICES.md) and the `licenses/`
folder supplied with redistributed application builds. This table indexes their terms.

## References and links

- [English manual](Documentation/Documentation.html#/en/overview)
- [Version notes and known issues](VERSION.md)
- [Source repository](https://github.com/MrMybal/CyMCPSDK)
- [Issue tracker](https://github.com/MrMybal/CyMCPSDK/issues)

<details>
<summary><strong>AI Usage</strong></summary>

- [Application integration prompt](Docs/IntegrationPrompt.fr.md) and [upgrade prompt](Docs/UpdatePrompt.fr.md) (French).
- [Primary agent guide](Documentation/DocAI.md) — architecture, build and integration contracts.
- [Detailed integration](Docs/Integration.md) — existing French developer reference.
- [Provider profiles](Docs/Providers.md) — existing French transport reference.

</details>
