# CyRevision showcase

Static English/French companion built from this application's real features, selected captures and existing CyDoc export. English is the default; `?lang=fr` selects French and language switching preserves chapter anchors and unrelated URL parameters. No executable is bundled.

## Build and open

From the application root with Node.js 22+:

~~~sh
node website/build.mjs
node website/build.mjs --check
~~~

Open `dist/website/index.html` directly in a browser, or copy the **entire** `dist/website` folder elsewhere and open it there. Keep `assets`, `manual`, `LICENSE` and `NOTICES.txt` together. The page uses classic JavaScript, local content-hashed assets, a system font stack and no runtime network requests. External GitHub/download links are optional. The script first uses the existing `.CyDocBuild/build.mjs --ensure` process; a stale manual may require npm to restore its checksum-pinned local CyDoc package. An already-built manual needs no network to be copied.

For optional HTTP/subpath testing:

~~~sh
node website/preview.mjs
~~~

Open `http://127.0.0.1:4173/cyrevision/` or its `?lang=fr` edition. The server binds only to loopback, serves only generated output and stops with Ctrl+C. Set `CYREVISION_SHOWCASE_PORT` if needed. HTML revalidates; content-hashed styles/scripts/images can be cached long term. A host must also revalidate HTML; asset hashing alone cannot refresh stale HTML.

This command does not publish. No GitHub Pages workflow, repository setting or release action is added. The output also works without Git metadata or a GitHub account and under any static project subpath without rewrites. Build refuses to replace unknown/edited generated files; preserve such files elsewhere before rebuilding.

## Maintained inputs

| File | Purpose |
| --- | --- |
| `content.mjs` | Complete EN/FR strings, real media catalog and verified optional release destinations |
| `template.mjs` | Prerendered English structure and contextual local reader links |
| `main.js` | Language, chapter, menu and in-page modal image behavior |
| `styles.css` | Graphite/orange responsive layout and reduced-motion support |
| `build.mjs` | Dependency-free build, manual lock verification, allowlisted copies and artifact checks |
| `preview.mjs` | Optional loopback/subpath preview, not a reading requirement |
| `test/showcase.test.mjs` | Structural, translation, artifact and simulated interaction tests |

Only these maintained sources are committed. `dist/website` is ignored and must be regenerated. Never edit the copied manual or its manifests: update `.CyDocBuild` inputs and regenerate the actual application manual. All contextual IDs are validated in both languages. `DocAI.md` is preserved as a separate sidecar, outside human navigation.

## Captures and diagrams

The catalog in `content.mjs` identifies exact source assets. The three complete screenshots under `docs/images` are real Windows V2 **0.1.32** captures of the disposable **AuroraFlight** project: workspace/C++ changes, revision history, and light-theme INI comparison. Captions distinguish them from the current **0.1.34** release; the screenshots retain the actual English application UI in both editions. No fabricated UI or promotional overlay is used. The five illustrated workflows reuse the existing English/French SVG diagrams under `.CyDocBuild/assets`; captions explicitly identify them as diagrams, not application captures. Build preserves original image bytes and aspect ratios.

The application and showcase use the repository's AGPL-3.0 license. The copied CyDoc reader retains its MIT license and source notice in `manual/DocData`. No CyUI code, editor engine or new media dependency is required for this showcase.

## Checks

~~~sh
npm ci --prefix .CyDocBuild --no-audit --no-fund
node --disallow-code-generation-from-strings --frozen-intrinsics --test website/test/*.test.mjs
node .CyDocBuild/build.mjs --check
~~~

Tests reuse the pinned Happy DOM development dependency already present in `.CyDocBuild/package-lock.json`; it is not shipped. DOM tests are not proof of visual or native-browser behavior. Before publication, open both `index.html` and `manual/Documentation.html` through `file://`, and check EN/FR at 1280×720, 1366×768, 1920×1080, 360/390px and tablet widths; also check 200% text/zoom, focus containment/restoration, Escape, image navigation, menu, scroll restoration, image loading, console errors and static-subpath links. Record results outside public content. The complete main capture should fit the desktop first screen; mobile and increased zoom should reflow and scroll.
