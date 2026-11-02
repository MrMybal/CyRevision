# Licensing and commercial distribution

CyUI is MIT licensed, including its optional first-party modules and the CEF /
ImGuizmo adapters. The [root license](../LICENSE) permits commercial use,
modification, redistribution and sublicensing, subject to retaining its copyright
and permission notice. It does not require an application's own source to be public.

Keep [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md) and the notices for the
dependencies actually included in your distribution. The bundled Dear ImGui and
TextEditor code retain their MIT notices, as do the stb headers inside the Dear ImGui
copy (`imstb_*.h`, MIT or public domain). CMake links that Dear ImGui copy whenever
the host does not name its own target in `CYUI_IMGUI_TARGET`. Optional host dependencies retain their
own licenses; they are not covered by CyUI's MIT grant.

For CEF, ship the license and Chromium credits/component notices belonging to the
exact runtime build. Check separately any additional codec/media packages chosen
by the application. CEF and its binary distribution are external dependencies,
not a browser engine relicensed under MIT by CyUI.

CyMD, CyAnnota, CyTask and other surveyed applications have independent licenses,
including GPL/AGPL in several repositories. Their source/assets are not copied
into CyUI. The optional browser bridge implements message transport and the
documented CyMD integration protocol. Loading or redistributing a separate Cy*
application does not change that application's license to MIT. Decide which
external applications to bundle under their own terms.

All new shared presentation implementations are written in CyUI against neutral
models/callbacks. The only newly bundled implementation dependency is the
MIT-licensed TextEditor snapshot with its original license and recorded hashes.
