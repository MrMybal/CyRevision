# Web panels with CEF

CyUI's optional Web module embeds HTML/CSS/JavaScript interfaces in a Dear ImGui
panel. TypeScript is compiled to JavaScript by the web application's own build.
The supplied browser engine is **CEF**, using off-screen rendering into a texture
owned by the host renderer. This avoids a platform-native child window covering
ImGui popups or docking panels. No browser dependency enters Core.

## Platform and validation matrix

| Component | Windows | Linux | macOS |
| --- | --- | --- | --- |
| Core and ordinary C++ modules | Portable sources; MSVC build/tests/render checked locally | Portable sources; CI configured, not run locally | Portable sources; CI configured, not run locally |
| Python models / imgui-bundle UI | Models and real ImGui frames checked locally | Same Python sources; target environment needs validation | Same Python sources; target environment needs validation |
| Web policy, messages, JS/TS declarations | Tested | Platform-independent code | Platform-independent code |
| WebCEF off-screen adapter | Compiled; real page, paint and native/Python message round trips tested | Shared implementation; build/runtime packaging unverified here | Shared implementation; framework/helper/AppKit packaging unverified here |
| Win32/DX11 demo and BMP capture | Implemented and tested | Host supplies its ImGui backend | Host supplies its ImGui backend |

CEF itself supplies Windows, Linux and macOS distributions. CyUI does not claim
that adding a CMake option validates a finished application on all three systems.
CEF was tested here with `150.0.23+g4ccc4ca+chromium-150.0.7871.287`, Windows x64.
Choose and validate a maintained CEF SDK for each target architecture.

## Build selection

```cmake
set(CYUI_BUILD_DEMO OFF CACHE BOOL "" FORCE)
set(CYUI_MODULES "Web" CACHE STRING "" FORCE)
set(CYUI_BUILD_WEB_CEF ON CACHE BOOL "" FORCE)
set(CYUI_CEF_ROOT "path/to/extracted/cef_binary_distribution" CACHE PATH "" FORCE)
add_subdirectory(external/CyUI)
target_link_libraries(app PRIVATE CyUI::WebCEF)
```

Alternatively set `CYUI_CEF_TARGET` to a host target providing the CEF headers,
runtime library and C++ wrapper. This is useful when the application already owns
CEF packaging. The adapter requires C++20 for the tested SDK. Core remains C++17.
No SDK/runtime download happens during configure or drawing.

The build also creates `cyui_webcef_bridge` (a C ABI shared library) and
`cyui_webcef_helper` for optional Python/foreign hosts. A host already running CEF
should use its own process/app integration rather than initialize a second runtime.

## Native lifecycle

1. Pass `cyui::webCEFApp()` to both `CefExecuteProcess` and `CefInitialize` before
   creating views. Use the same renderer handler in all helper executables.
2. Set `CefSettings.windowless_rendering_enabled = true`. The application owns
   CEF initialization, sandbox policy, cache/profile directory and subprocess paths.
3. Supply an upload callback accepting an upper-left-origin BGRA8 buffer. Update
   a reusable host GPU texture and return its `ImTextureID`. The buffer expires
   after the callback. The texture remains host-owned. The optional `release`
   callback runs once, when the browser has closed, with the last texture returned
   by `upload`: the view no longer draws it. Free it only once no submitted frame
   references it (renderers with frames in flight defer by that many frames).
4. Call `CefDoMessageLoopWork()` once per loop iteration **outside** the ImGui frame
   (before `NewFrame`, never between `NewFrame` and `Render`). `upload` and `release`
   run inside it, so texture updates and releases never race a draw list that is
   being built. Then `NewFrame`, `beginFrame()`, `webPanel`, `endFrame()`, `Render`.
   The end call hides an undrawn tab/panel.
5. Call `close()` on all views and keep pumping until `closed()`. Destroy views
   before GPU resources, then call `CefShutdown()` on the initializing thread.

Every backend call must run on the CEF UI thread (the thread that called
`CefInitialize` with the single-threaded loop). Calls from another thread are
ignored and return false, construction throws; debug builds also assert.

```cpp
#include <cyui/adapters/WebCEF.h>

cyui::WebCEFOptions options;
options.url = "http://127.0.0.1:4173/chat";
options.policy.origins = {"http://127.0.0.1:4173"};
options.upload = [&](int width, int height, const void* bgra) {
    return renderer.updateBrowserTexture(width, height, bgra);
};
options.release = [&](ImTextureID texture) { renderer.releaseAfterFramesInFlight(texture); };
auto web = cyui::createWebCEF(options); // after CefInitialize

// Each host loop iteration, on the same thread:
CefDoMessageLoopWork();       // outside the ImGui frame
ImGui::NewFrame();
web->beginFrame();
if (ImGui::Begin("Chat")) {
    for (const auto& event : cyui::webPanel("##chat", *web, {0, 0})) {
        if (event.kind == cyui::WebEventKind::navigated)
            web->postJson(cyui::webThemeJson());
        // Parse/validate application messages; apply host commands explicitly.
    }
}
ImGui::End();
web->endFrame();
ImGui::Render();
```

The default software paint path bounds each view to 4096×4096 pixels and 30 fps;
options allow limits up to 8192. HTML select popups are composited into the same
texture. webPanel forwards mouse buttons (with the press's click count on release,
so `dblclick` fires), both wheels, Unicode characters and keys: letters, digits,
F1-F24, punctuation, keypad, navigation/editing keys and left/right modifiers. On
Windows the adapter adds the active layout's scan code, so the page receives
`KeyboardEvent.code`. While the panel is focused it owns those keys (ImGui key
ownership): Tab, arrows, Space, Enter or Escape do not also move ImGui keyboard
navigation or activate other widgets. `WebPanelOptions::focus` is one-shot: focus
is taken when it changes from false to true, so a click elsewhere can still
unfocus the page. The host must keep platform text input enabled while the
backend is focused. Use `webCEFBrowser()` for platform-native key events,
IME composition/candidate placement, accessibility, external file drag/drop or
other advanced CEF integrations. These platform features are host responsibilities,
not validated by the basic pointer/text/message tests.

CEF message pumping is intentionally single-threaded here. Do not enable CEF's
multi-threaded message loop and then invoke this backend from a different ImGui
thread. Background engine work stays in CEF's subprocesses.

## Browser boundary

WebPolicy accepts exact HTTP(S) origins, including ports. It rejects credentials,
file/javascript URLs, wildcard matching, malformed ports and ambiguous backslashes.
Use a loopback server for a built local frontend or an explicitly permitted HTTPS
origin. CyUI does not start the frontend's server, compile TypeScript or choose its
authentication. CEF retains normal browser networking for page subresources.

Main-frame navigation outside allowed origins and new-window requests (popups,
`target=_blank`) are never opened by the adapter; they become `external` events.
An `external` event is a request from page content, not a command:

- Only schemes in `WebPolicy::externalSchemes` (default `http`, `https`) are
  reported; others (`file:`, `ms-settings:`, custom protocol handlers...) are
  dropped. Add a scheme only deliberately.
- Popups from sub-frames (including cross-origin iframes) are dropped unless they
  come from a user gesture. Events carry `userGesture`, `mainFrame` and the
  requesting frame's `sourceOrigin`.
- Before handing a URL to `ShellExecute`, `open` or a system browser, check it
  against your own scheme/host allow-list and ask the user to confirm. Never pass
  an event URL to the shell unchecked.

Messages are accepted only from the main frame of an allowed origin and are size
checked before parsing. The page-side limit in the renderer follows
`maxMessageBytes` (default 1 MiB, at most 64 MiB). Host messages (`postJson`) are
bound to the origin loaded when they were sent: the renderer drops them if the page
has navigated to another origin meanwhile. The event queue holds `maxQueuedEvents`
(256); error/console events have a separate lower cap, `maxQueuedErrors` (16), so a
noisy page cannot evict messages. Anything discarded (full queue, caps, oversized
data, refused schemes or popups) is counted and reported by a `dropped` event whose
data is the count. Downloads, media permissions and other permission prompts are
denied by the supplied minimal adapter; enable product-specific capabilities in an
explicitly reviewed host integration if required.

### Process sandbox

**The supplied binaries run CEF without its process sandbox on Windows.** Windows
sandboxing needs sandbox information created by the executable's entry point; the
`cyui_webcef_bridge` library and the generic `cyui_webcef_helper` pass none, so the
bridge sets `no_sandbox = true` explicitly. A sandbox request (Python
`no_sandbox=False`, the default) is reported, not honoured: `cyui_cef_sandboxed()`
returns 0, `cyui_cef_error()` explains it, and `CEFRuntime` issues a
`RuntimeWarning` and sets `sandboxed = False`. On Linux the request is passed to
Chromium (which needs `chrome-sandbox` or user namespaces); on macOS the helper
bundle must also initialize `CefScopedSandboxContext`. Neither is validated here.

To sandbox a Windows host with the tested CEF 150 SDK, use CEF's bootstrap layout
(see the SDK's `cef_sandbox_win.h` and CEF's sandbox guide): ship `bootstrap.exe`
(or `bootstrapc.exe` for console apps) renamed to your application, build the
application and its subprocess entry as a DLL exporting `RunWinMain` /
`RunConsoleMain`, and pass the `sandbox_info` it receives to both
`CefExecuteProcess` and `CefInitialize`, with `no_sandbox = false`. Older SDKs that
ship `cef_sandbox.lib` instead link it into the executable and create
`CefScopedSandboxInfo` in `wWinMain`. The browser and every helper must use the
same arrangement. Validate it on the target system before claiming sandboxing.

## Browser JavaScript / TypeScript

`web/` is the MIT `@cyui/web` package with JavaScript, TypeScript declarations and
an optional CSS stylesheet. CEF installs a narrow `window.cyuiNative` message
transport. No Electron or Node API is exposed to the page.

```ts
import {createCyUIBridge} from '@cyui/web';
const bridge = createCyUIBridge();
bridge.on('host.reply', payload => updateView(payload));
bridge.send('chat.message', {text: 'Hello'});
// Dispose listeners when the embedded UI is destroyed.
bridge.close();
```

Messages contain `{protocol: 'cyui.web', version: 1, type, payload, requestId?}`.
The application owns message schema, authorization and effects. A theme message
from webThemeJson()/theme_message() supplies palette CSS variables such as
`--cyui-bg0`, `--cyui-text` and `--cyui-accent`; send it again after a theme change.
JSON is transported as data through CEF process messages, without interpolating
host text as executable JavaScript.

## CyMD, CyAnnota and the other applications

`mountCyMD()` implements CyMD's existing version-1 iframe protocol: explicit
parent origin, frame identity and session checks, ready/open/save/state messages,
Blob documents and save acknowledgments. Serve a small host page and the built
CyMD frontend over HTTP(S), then mount it in that page. The host supplies onSave;
CyUI does not write files. Native JSON messages do not carry Blob objects directly:
the host page handles Blob/document transfer and can use a host document service
or an explicitly bounded encoded/chunked protocol.

CyAnnota and CyTask can be loaded as built web frontends with their required
services/adapters. Their Electron preload/IPC APIs must be replaced by explicit
host messages where needed. CyAIOrchestrator already has a web frontend. CyImgView
can host these web tools through a panel while using the native Images module.
No application is migrated or modified by this change. Each retains its own license.

## Python

```python
from cyui_modules.cef import CEFRuntime
from cyui_modules.web import web_panel, theme_message

with CEFRuntime(library_path, helper_path, absolute_cache_path, no_sandbox=True) as runtime:
    view = runtime.create(url, (allowed_origin,), upload_bgra_to_host_texture,
                          release=release_texture_later)
    # Each loop iteration:
    runtime.pump()            # before new_frame: uploads/releases happen here
    imgui.new_frame()
    runtime.begin_frame()
    events = web_panel('chat', view, (0, 400))
    for event in events:
        if event['kind'] == 'navigated':
            view.post_json(theme_message())
    runtime.end_frame()
    imgui.render()
```

All runtime/view calls must run on the owning UI thread. `begin_frame()` does not
pump; call `pump()` outside the ImGui frame. The context manager closes and pumps
all views before shutting CEF down; shutdown fails while a view is alive, and CEF
cannot be initialized again in the same process. An upload callback receives a
native pointer valid only during the call; copy/update immediately. `release`
runs from `pump()` after the view closed. `no_sandbox=True` states what the bridge
does on Windows anyway (see Process sandbox); leaving the default warns. Python's
garbage collector does not initialize or shut down a browser on an arbitrary thread.

## Deployment and checks

Native `webThemeJson()` and Python `theme_message()` also carry the current UI
locale (English by default). Send a fresh theme after changing languages. The
JavaScript bridge translates only marked interface elements and exposes a catalog
for TypeScript components; see [LOCALIZATION.md](LOCALIZATION.md). Bundle the
bridge's `cyui-i18n.js` and `cyui-locales.js` imports with the main bridge module.

- Windows: ship matching libcef/Chromium runtime files, resources/locales, helper
  executable and supported sandbox setup. The optional test/demo CMake staging
  copies the selected SDK's Release/Resources into the build output.
- Linux: ship/use matching libcef.so, resources/locales and helper; configure the
  runtime search path, target display stack and Chromium sandbox as CEF requires.
- macOS: package/load Chromium Embedded Framework.framework and correctly named
  signed helper app bundles, integrate CEF's AppKit application handling, entitlements
  and sandbox setup. The generic helper source is not a complete macOS app bundle.
- Keep the selected SDK's LICENSE.txt, CREDITS.html and other component notices.
  See [LICENSING.md](LICENSING.md).

```sh
cmake -S . -B build/cef -DCYUI_BUILD_DEMO=OFF -DCYUI_MODULES=Web \
  -DCYUI_BUILD_WEB_CEF=ON -DCYUI_CEF_ROOT=path/to/cef -DCYUI_BUILD_TESTS=ON
cmake --build build/cef --config Release
python -B tests/run_webcef.py build/cef/cyui_test_webcef.exe
```

Use the platform's executable/library paths for other systems. The local test
serves only its fixture routes, checks native and Python page/paint/message round
trips, double-click and `KeyboardEvent.code` delivery, and deletes its temporary
profiles afterwards. The full CMake Win32 demo also accepts
`--web-url http://127.0.0.1:PORT/examples/web/index.html --tab 19` when built with
CEF. It passes no sandbox info either, so it is not sandboxed with or without its
`--cef-no-sandbox` switch; production hosts provide their own sandbox integration.

Reference: [CEF general usage](https://chromiumembedded.github.io/cef/general_usage.html),
[CEF binary distributions](https://cef-builds.spotifycdn.com/index.html).
