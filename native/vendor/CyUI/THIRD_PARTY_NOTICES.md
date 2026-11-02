# Third-party notices

CyUI's own sources, documentation, Python modules, browser bridge and adapters
are distributed under the root [MIT license](LICENSE). Dependencies retain their
own copyright and licenses. Selecting a module does not relicense its dependencies.

| Component | License | Included / optional | Notice |
| --- | --- | --- | --- |
| Dear ImGui | MIT | Bundled: built and linked by CMake when `CYUI_IMGUI_TARGET` is empty, and by the demo; hosts normally supply their own target | [third_party/imgui/LICENSE.txt](third_party/imgui/LICENSE.txt) |
| stb_truetype, stb_rect_pack, stb_textedit (Sean Barrett), as `imstb_*.h` | MIT or public domain (Unlicense), at the user's choice | Bundled inside the Dear ImGui copy and compiled with it | License text at the end of each [third_party/imgui/imstb_*.h](third_party/imgui) file |
| ImGuiColorTextEdit, Johan A. Goossens | MIT | Bundled only for the optional C++ CodeEditor target | [third_party/texteditor/LICENSE](third_party/texteditor/LICENSE) |
| Lexilla, selected lexical engines | HPND-style permissive license (Neil Hodgson and contributors) | Bundled and compiled only with Code or its dependents; no Scintilla editor is linked | [Lexilla license](third_party/lexilla/License.txt) |
| Scintilla public headers | Same permissive license | Header-only interface for Lexilla; no Scintilla UI sources | [Scintilla license](third_party/scintilla/License.txt) |
| SciTE keyword/property data | Same permissive license | Generated built-in language profiles | [SciTE properties license](third_party/SciTE-Properties-License.txt) |
| ImGuizmo | MIT upstream | Optional host-supplied target | Preserve the license of the exact host dependency |
| Chromium Embedded Framework | BSD-style, three conditions | Optional host-supplied SDK/runtime; no CEF binaries in this repository | Preserve the selected SDK's LICENSE.txt and Chromium CREDITS.html, plus its component notices |
| imgui-bundle | MIT package, with separately licensed bundled components | Optional external Python UI dependency | Preserve notices in the installed package/distribution |

The TextEditor source is a pinned upstream snapshot. File hashes, upstream URL
and the serialization patch are recorded in
[VERSION.json](third_party/texteditor/VERSION.json). The patches preserve an absent
final newline and add revision-aware shared syntax spans; the original copyright and MIT license remain intact.

CEF is distributed separately for each OS/architecture. Its Chromium components
have additional notices and licenses; the CEF BSD license alone is not the complete
notice set for an application shipping the runtime. Preserve the distribution's
license/credits files, and review the exact codec/media build you choose to ship.
No CEF runtime is downloaded automatically by CyUI's CMake configuration.

System fonts (for example Segoe UI on Windows) are loaded from the operating
system or host-provided font paths. CyUI does not distribute those font files.

The optional Unreal Editor connector uses the host's Unreal Engine installation.
Unreal Engine is separately licensed by Epic; no engine source or binary is
included in CyUI or the prepared plugin's source snapshot. CyUI's own connector
remains covered by the root MIT license.

The Cy* applications surveyed for common UI requirements are not incorporated
dependencies. Their code, assets and web builds retain their separate licenses.

Syntax source revisions and archive hashes are recorded in
[code-syntax.lock.json](third_party/code-syntax.lock.json). Generated language data
comes from the pinned SciTE properties and original CyUI shader profiles, with
notices retained. No Notepad++ source, GPL configuration or plugin binary is bundled.
The built-in list describes syntax modes; it does not imply a compiler, language
server, debugger or compatibility with the Notepad++ plugin ABI.
