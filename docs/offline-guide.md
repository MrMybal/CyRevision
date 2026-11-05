# Local CyDoc documentation

Both desktop interfaces open the same packaged **CyDoc** reader in the system browser using a `file://` URL. **Help > User guide** opens the home page; **F1** and contextual help open the active workspace's chapter. Help works without a project and during project operations. No HTTP server, CDN, browser engine or Node.js runtime is shipped for reading.

## Content and languages

`documentation/manual/en` and `fr` contain 48 matching guides. Frontmatter page IDs and explicit heading IDs are shared between translations. English is the default. The host chooses the application language, then English, then the original language for references that have no English version. CyDoc owns search, history, themes, language switching and reading positions. Browser storage restrictions can prevent position persistence; navigation still works.

`documentation/references.json` imports 38 existing technical documents and historical release notes without replacing their original sources. Historical pages are labelled. References retain their maintained language: 26 need French translations and 12 need English translations. The generated **References and coverage** page records these gaps and repository-only exclusions. CyDoc's standard translation report tracks the English originals (26 missing, zero outdated); three bilingual annotation pages are untracked because the pinned extractor does not carry revision metadata.

Guides use `revision` (English) and `sourceRevision` (French) to track synchronization. Preserve page, heading and image IDs when translating. `documentation/routes.json` maps workspaces and the former Help tab to page IDs. Legacy `doc:page-id#section` references and configured aliases remain supported. Reader routes are `#/locale/page-id/section?media=image-id`.

## Modules, media and source API

Only **image** is enabled: screenshots, localized SVG diagrams, zoom, annotations and in-reader enlargement. Static diagrams do not require graph. Existing content has no videos, animated guides, 3D models or interactive node graphs, so video, gif, mesh, gallery and graph are omitted. Three.js is not an application dependency. Escape exits enlarged media without creating a window; CyDoc supplies keyboard focus, touch interactions and zoom controls.

Assets live in `documentation/assets`. `scripts/render-documentation-media.py` generates 13 English/French SVG diagrams and PNG companions from `scripts/documentation-diagrams.json`, and copies demonstration screenshots from `docs/images`. Pillow is an authoring dependency only. Add local media using CyDoc fenced image blocks with stable IDs and translated captions.

`documentation/source-scope.json` limits the distributed source index to two public C# plugin-contract files and the native documentation-link contract. Bilingual `@doc` annotations generate API pages. The index is lexical: no interpretation of C# XML comments, overload resolution or C++ macro expansion. Server API and plugin examples are also retained as references. Application data, validation, services and Undo/Redo remain outside CyDoc.

## Generate and check

Generation requires **Node.js 22+** and npm. The official CyDoc npm archive is vendored and pinned by revision and SHA-256 in `documentation/cydoc.lock.json`. Builds do not require private GitHub access. npm installs locked authoring/test dependencies when needed; installed applications need neither npm nor Internet access.

From the repository root:

```sh
node documentation/build.mjs
node documentation/build.mjs --check
node --test documentation/test/*.test.mjs
dotnet test CyRevision.sln -c Release
```

`python scripts/build-documentation.py [--check]` remains a compatibility wrapper. `--ensure` validates inputs and output hashes and rebuilds only when needed. Desktop builds run it before copying resources. The official exporter resolves resources and bundles the image provider; the host adds a CSP, palette, favicon, license and coverage metadata without modifying library bundles or compiled page data.

The generated, versioned reader is `src/CyRevision.Desktop/Help`. Desktop build/publish outputs copy it recursively, including portable packages and installers. Keep the **whole Help directory**: data.js, viewer.js, media-image.js, styles and assets. Regenerate after changing inputs. Generated staging folders `documentation/content-build`, `documentation/source-api` and `artifacts/cydoc-export` are not sources.

## Integration verification

Managed tests cover every workspace route, locale fallback, legacy aliases, sections/media IDs, installation paths with spaces and non-ASCII characters, unavailable readers and help during busy operations. Native tests cover F1, callbacks, locales, the launch surface and non-reader URL rejection without opening a browser. Node tests validate compiled links/resources, translation parity, source scope and modules. A DOM fixture exercises actual CyDoc navigation and image controls with script/resource loading and navigation disabled; it is not a browser rendering test.

For a local published build, check real IPC without opening a browser:

```sh
python scripts/test_offline_guide.py --engine artifacts/native-v2-cydoc/CyRevision.Desktop.exe
dotnet run --project tests/CyRevision.Guide.UiChecks -c Release -- artifacts/cydoc-verification
```

The second command checks/captures the Classic launch surface only. Before shipping on each target, manually open Help/index.html and test F1/context links, both languages, search, anchors, image focus/enlargement/Escape, touch/zoom, themes, narrow layouts, DPI and reading-position restoration. Verify offline with networking disabled. Real file:// transport, image decoding and browser storage require a target browser; DOM fixtures cannot establish those properties.

Windows host builds and automated tests are available. Linux/macOS retain the Classic publish path but need target-platform browser/rendering checks. Native V2 remains Windows/Direct3D 11. CyDoc source/license: [MrMybal/CyDoc](https://github.com/MrMybal/CyDoc) (MIT; repository access may be required).
