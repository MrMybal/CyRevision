# CyRevision documentation maintenance

This is an authoring note, not a page in the human manual. All paths below are relative to the application repository.

## Layout and distribution

- `.CyDocBuild/manual/en` and `fr`: matching authored guides and API overviews (58 page IDs).
- `.CyDocBuild/assets`: screenshots, application logo and localized diagrams.
- `.CyDocBuild/api`: structured public API descriptions, signature validation and the documented CyDoc API-import adapter. It generates 28 function pages per language without publishing implementation files.
- `.CyDocBuild/internal`: working notes and maintenance instructions, excluded from the reader.
- `.CyDocBuild/.cache`: disposable API, content and export staging, ignored by Git.
- `Documentation/Documentation.html`: reader entry point.
- `Documentation/DocData`: complete reader scripts, styles, data, providers, routes, media and license notices.
- `Documentation/DocAI.md`: agent-oriented index, separate from reader navigation and search.

Distribute the entire Documentation directory, never .CyDocBuild or node_modules. Native V2 and Classic open the same file URL in the system browser. Reading needs neither Node.js, a server, a CDN nor an Internet connection. The desktop project generates before copying content during build and publish; release packaging inherits that directory.

## Dependency

CyDoc is pinned to revision `6ddf9a582defb2f11fbb3f992afef6f98dcadc93` in `cydoc.lock.json`. The official npm archive is vendored and SHA-256 checked before use. Its license and source notice accompany the export. Builds do not need access to the private Git repository. Node.js 22+ and npm are authoring/build dependencies; the lockfile pins the authoring/test packages.

Only image and gallery are selected. Screenshots and static SVG diagrams use image; the interface tour uses gallery with image children. Video, gif, mesh and graph are not used. Three.js is not installed in this application's documentation toolchain or shipped reader.

The reader shell in `reader.html` sets the application title, English document language, favicon and local-resource CSP. It uses the unmodified CyDoc viewer and styles, including the wider hierarchical sidebar, top search, tooltips, six themes and reading positions. Do not patch generated bundles or compiled content.

## Authoring

English is original/default. Every page, title, description and media explanation must have a complete French counterpart with matching IDs and explicit anchors. Increment the English revision and update the French sourceRevision together once translated. Generation rejects missing or outdated translations. Use parent for real page relationships and section for localized navigation group paths.

Keep build instructions, test reports, migration inventories and agent instructions outside manual. Historical technical-reference links are aliases to current user-facing topics, not duplicate report pages. DocAI is exported separately.

The public API catalog documents exact C# signatures, parameters, returns and relationships using CyDoc's structured importer. Signature validation reads only the files in `api/source-scope.json`; those files are not copied into the export. This lexical check is not a C# compiler, XML-documentation parser or overload resolver. Native help IPC is described by an authored reference. No SourceFile browsing is enabled.

Use `python .CyDocBuild/render-media.py` to regenerate the 13 diagram pairs and PNG companions from `.CyDocBuild/diagrams.json` and copy the application screenshots. Pillow is needed only for this optional media-authoring command. Translate media captions and alt descriptions; preserve media IDs.

## Regenerate and validate

From the repository root:

```sh
node .CyDocBuild/build.mjs
node .CyDocBuild/build.mjs --check
node --test .CyDocBuild/test/*.test.mjs
dotnet test CyRevision.sln -c Release
```

The first command generates through the official exporter into isolated staging, then copies the complete export. --check also invokes the official exporter (CyDoc check writes an export) but compares staging against the packaged output without replacing it. --ensure checks input and output hashes and regenerates only when necessary. The compatibility wrapper `python scripts/build-documentation.py [--check]` calls the same pipeline. Unknown files in Documentation are preserved by refusing replacement rather than deleting them silently.

Help > User guide opens the home page. F1 and contextual help use `routes.json` for the current workspace; legacy aliases, common anchors and media IDs are validated before producing the file URL. Help remains usable without a project or while project operations are running.

After publishing a local desktop build, test its actual IPC link generation without launching a browser:

```sh
python scripts/test_offline_guide.py --engine artifacts/native-v2-cydoc-update/CyRevision.Desktop.exe
dotnet run --project tests/CyRevision.Guide.UiChecks -c Release -- artifacts/cydoc-update-checks
```

The second command checks and captures only the Classic help launch surface. Node DOM fixtures disable script/resource loading and navigation; they test viewer events and state, not browser transport or pixel rendering.

Before distribution, open Documentation/Documentation.html in a target browser and verify local loading, F1/context links, both languages, Ctrl+K, nested navigation, full-title tooltips, gallery captions, image zoom/enlargement/Escape, touch, narrow layouts, DPI, themes and reading-position restoration. Browser storage policies may affect position persistence. Repeat browser and packaging checks on Windows, Linux and macOS; the native V2 executable is Windows/Direct3D 11 only.
