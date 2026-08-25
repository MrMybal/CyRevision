# Changelog

## Unreleased

## 0.1.21 Alpha — 2026-08-25

- redesigned the GitHub pull-request workspace with numeric sorting, colored state/ownership/CI indicators, separated description/files/conversation/CI/conflicts/actions views, accurate active-job aggregation, and permission-aware rerun or cancellation actions;
- added complete GitHub Actions logs to both CI and pull-request workflows with error/warning classification, combined search and severity filters, horizontal scrolling, consistent monospace presentation, and a synchronized non-modal large-log window;
- added non-destructive pull-request conflict inspection through disposable private references plus confirmed cleanup of merged local branches after Git safety analysis;
- expanded the project-scoped Jira and ClickUp plugins with automatic task-link detection and configurable confirmed or automatic completion transitions after a successful merge;
- repaired and refined alternative Git history visualizations with stable pan/zoom/fit behavior and a calendar-style activity heatmap with contributor and hotspot summaries;
- refreshed the application and repository branding, and made explicit exit confirmation perform bounded service cleanup so quitting closes the complete CyRevision process reliably.

## 0.1.20 Alpha — 2026-08-25

- added the optional, project-scoped **Git + CyStore — ALPHA** mode: standard Git and Git LFS remain authoritative while hydrated LFS files can be captured into a verified, content-defined, deduplicated local chunk store and reconstructed non-destructively on demand;
- added a read-only branch file explorer with fast path search, multi-selection, cancellable targeted retrieval, temporary-ref cleanup and a persistent audit of exports and safe restores;
- added side-by-side image and heatmap comparisons plus plugin-provided semantic previews and Unreal asset inspection across the shared file-presentation pipeline;
- split branch-file, CyStore and file-presentation operations out of the main desktop view model to reduce UI coupling and keep long repository work cancellable;
- added a safe missing-`.gitignore` recommendation flow with editable Unreal, Unity, Godot, .NET and Node.js presets;
- added the five-step **Initialize Git** wizard for tool checks, `.gitignore`, `.gitattributes`/Git LFS recommendations, repository-local identity, optional origin and an optional reviewed initial commit;
- added a reusable Git LFS setup assistant that merges recommended patterns into `.gitattributes` without overwriting existing rules or staging files implicitly.

## 0.1.19 Alpha — 2026-08-20

- added safe local-branch removal, protected-reference analysis, external LFS storage and verified cleanup plans that preserve retained branches and never delete an unverified last copy;
- added Git annotations, guarded large selections and temporary pathspec files so large commits no longer exceed Windows process limits;
- completed remote repository cloning with destination selection and project registration from the desktop interface;
- expanded the autonomous Unreal revision provider with revision history, file history, diffs, restores and safer writable-file behavior;
- added project-scoped Jira and ClickUp plugins with API-backed task search, multi-selection and stable task links for commit messages and pull-request drafts without persisting API tokens;
- made Windows packaging reuse an already verified Syncthing runtime while clean builders still download and validate the official SHA-256 digest.

## 0.1.18 Alpha — 2026-08-18

- added a project-scoped Perforce integration plugin with connection discovery, workspace status, changelists, opened files, reconcile, sync and guarded submit workflows;
- added a complete three-panel Git conflict resolver with editable results, block-level choices, syntax-aware previews, optional AI guidance and retained recovery backups;
- introduced Sync + Commit as a Git-free project mode with commit-time exchange, versioned snapshots, conflict detection and explicit resolution before publication;
- expanded Backup with guided hot-to-cold archive profiles for old Git commits, branches and synchronized versions, keeping every destructive cleanup opt-in and restorable on demand;
- enabled plugins to contribute project operating modes and dedicated workspaces, including the optional Lore project-management mode;
- refined the Changes workspace with an additional compact review layout and clearer mode-aware navigation;
- hardened Windows release packaging with a PowerShell-independent SHA-256 implementation for runtime and artifact verification.

## 0.1.17 Alpha — 2026-08-17

- fixed the Linux validation build by forcing synchronization-history reversal through LINQ instead of the platform-specific in-place array overload;
- aligned local and GitHub Actions builds on the .NET 9 SDK required by Avalonia 12.1 source generators, while application binaries continue to target .NET 8;
- added a repository `global.json` so developer, validation and packaging builds use the same compiler generation.

## 0.1.16 Alpha — 2026-08-17

- made plugin activation project-scoped, including project-specific tabs and capabilities without disrupting plugins used by other open projects;
- added optional Unity and Godot integrations with project detection, installation helpers and local CyRevision bridges;
- added an optional Lore project-management integration with a CyRevision control surface and bundled Unreal plugin installer;
- expanded Unreal asset inspection with faster cached metadata, semantic Material and package differences, Content Browser filters and work-in-progress indicators;
- reorganized workspaces by operating mode: Git tools are hidden outside Git modes, Backup remains directly available, and Git + Sync receives a dedicated signed-exchange workspace;
- added optional shared-folder synchronization to every project plus mode-specific Sync + Versions configuration, searchable synchronization history and a guided setup wizard;
- added safe synchronization conflict resolution with retained recovery copies and per-file history entry points;
- improved team chat with a configurable self-hosted server backend, richer messages and persistent project-scoped conversations;
- fixed excessive empty space in Solution Explorer and clarified project-specific plugin activation in the interface and documentation.

## 0.1.15 Alpha — 2026-08-15

- added project-scoped AI conversations with persistent history, optional Markdown rendering, reusable prompts, isolated worktrees and automatic local Codex availability detection;
- added AI-assisted commit and pull-request descriptions plus file summaries, while preserving explicit project permissions and read-only defaults;
- added detachable synchronized workspaces across Git, code, assets and administration tools, with double-click opening and independent window stacking;
- expanded project administration with persistent ordering and accent colors, safe catalogue removal with optional CyRevision cache cleanup, and a complete project-license editor;
- redesigned Overview with dedicated Members, Visible tabs and License pages, per-project visibility presets and always-accessible recovery controls;
- improved the change editor with recursive folder selection, expanded folder trees, sortable checked/name/state/lock/area columns, per-project column visibility and compact file/path presentation.

## 0.1.14 Alpha — 2026-08-15

- expanded the Unreal integration across UE 4.27–5.8 with C++ and Blueprint-only installation paths, a native revision-control provider, compact toolbar and context-menu tools, lock/WIP views, compatibility diagnostics and safer writable-file behavior;
- added optional headless Unreal asset inspection with 512 px mesh thumbnails, package metadata, Blueprint semantic summaries and plugin-provided preview/diff handlers shared by every CyRevision file view;
- introduced the Unreal local build lab with target and engine discovery, Windows/Linux/Android profiles, toolchain guidance, cancellable builds, dense live logs, diagnostics and cached discovery;
- added project-scoped team chat over VPN or Sync, encrypted incremental storage, optional conversation archives, image/file transfers and richer member visibility;
- added historical worktree and branch-from-commit workflows, improved global tasks, notifications, compact layouts and English/French localization coverage;
- added a project-scoped Codex chat using the local Codex App Server, automatic desktop/CLI detection, streamed responses, cancellation and explicit read-only or workspace-write permissions;
- bounded large text previews to 256 KiB, moved decoding and symbol extraction off the UI thread, and switched very large documents to a lightweight renderer to prevent interface freezes.

## 0.1.13 Alpha — 2026-08-14

- accelerated very large repositories with batched virtualized collections, bounded caches, cancellable Git processes, persistent project snapshots and a low-cost background change monitor that avoids rescanning when switching projects;
- redesigned file and code discovery with dense Rider-style result lists, wildcard-aware path filters, secondary result filtering, lazy project trees, responsive previews and visible non-disruptive background progress;
- unified file presentation across the solution explorer, changes, history and detached diff windows, including native image previews and plugin-provided preview/diff handlers for additional asset formats such as Unreal packages;
- expanded commit composition and ignore-rule workflows with tracked/untracked separation, safer default selection, local-only files, folder and file-type assistants, glob patterns and complete `.gitignore` editing;
- added GitHub Actions inspection and dispatch support, richer project-scoped logs/tasks, safer Git lock diagnostics and performance telemetry for long-running operations;
- added independent per-project Syncthing shared folders with send/receive modes, persistent configuration and Git-only compatibility, plus clear Local Git/Git + remote status;
- simplified secure WireGuard onboarding with named clients and compact signed invitation/response codes while keeping private keys on the client and preserving optional Sync-based exchange.

## 0.1.12 Alpha — 2026-08-14

- fixed the architecture validation command for the Xcode 15.4 `lipo` syntax used by GitHub's macOS runners;
- preserved the native-launcher bundle layout introduced in 0.1.11 and now allows the workflow to continue into signing and DMG creation.

## 0.1.11 Alpha — 2026-08-14

- corrected the incomplete macOS signing fix from 0.1.10 by moving managed assemblies and application resources out of `Contents/MacOS` into `Contents/Resources/app`;
- added a minimal architecture-specific native launcher as the sole `CFBundleExecutable`, preserving self-contained Intel and Apple Silicon packages while allowing strict bundle signing;
- retained explicit nested Mach-O signing for the desktop runtime, bundled Syncthing runtime, Discord agent and remote build agent before sealing the application envelope.

## 0.1.10 Alpha — 2026-08-14

- fixed macOS packaging on both Intel and Apple Silicon by signing nested native binaries before the application envelope and letting `codesign` sign the main executable with its bundle;
- updated the release workflow to current Node 24-based checkout and .NET setup actions and aligned every release default on version 0.1.10;
- added the optional managed Syncthing workspace with per-project configuration, send/receive modes, ignore rules, connected-device state and project-scoped logs without requiring a preinstalled executable;
- improved large-repository responsiveness with lazy solution-tree expansion, retained project sessions, cancellable background work and a persistent cross-workspace task list;
- expanded Linux-style project diagnostics for Git, LFS locks and long-running operations, including safer stale `index.lock` handling without deleting locks owned by active Git processes.

## 0.1.9 Alpha — 2026-08-14

- redesigned the Changes workspace for large repositories with dense list and folder views, tracked/untracked/local-only composition, lock ownership, selective staging, rollback and commit controls;
- expanded branch inspection with local/remote publication and divergence state, search and sorting, inferred creation metadata, selected-branch commits and direct commit-explorer navigation without switching branches;
- added a standalone Commit Explorer with searchable revisions, committed files, file history, selectable layouts, detachable live diff and reusable entry points from History, Branches, Pull Requests and Compose;
- expanded Pull Request browsing with provider loading state, remote request discovery, status/details, files, patches, conversations and commit-explorer integration;
- added a dedicated searchable Git LFS file-lock workspace with list/folder views, independent filters and sorting for project and personal locks, multi-selection and confirmed bulk unlock operations;
- added a full Solution Explorer with tree/list views, file search, syntax-aware preview, excluded heavy build caches, automatic first load and configurable low-frequency refresh;
- added project-scoped repository consoles with persistent command history plus searchable daily application logs stored outside repositories;
- added per-project workspace persistence for the active tab, console/log section and code refresh policy;
- added in-memory project session snapshots so returning to an already loaded project does not rescan Git, LFS or project tools; explicit Refresh and Fetch remain the source of updates;
- accelerated text and commit diffs with cancellation of obsolete requests, bounded rendering, per-file/revision caches, independent history loading and faster no-rename Git queries;
- improved large-project responsiveness with asynchronous loading, visible progress, corrected project/branch identity and safer handling of inaccessible directories.

## 0.1.8 Alpha â€” 2026-08-13

- reorganized the desktop into clear Overview, Git, Code & Assets, Team & Network, and Extensions categories while keeping direct menu navigation;
- added a project-wide member overview for authorized Sync identities, Git contributors, and configured VPN peers with live or last-known status;
- added Rider-style syntax coloring to the code explorer, file-type coloring in the project tree, and a denser revision history;
- expanded Multi Restore with a permanent commit timeline, automatic file loading, resizable panes, and multiple composition layouts;
- added a dedicated top-level Git File locks workspace listing verified project locks and personal locks, with individual unlock, unlock-all-mine, confirmed administrative force unlock, and clearly marked offline cache fallback;

- added a safe per-file Multi Restore composer with before/at-commit choices, rename/add/delete planning, local-change confirmation, LFS availability checks, expiring previews, timestamped recovery copies, and no implicit index or commit changes;
- added patch-equivalent branch comparison and an ordered cherry-pick composer with keep-or-combine modes, automatic rollback, no automatic push, and temporary worktrees for inactive target branches;
- added reusable detachable History, Code, Multi Restore, and Cherry-pick workspaces for multi-monitor use, plus a denser Rider-style project tree and project sidebar;
- added a native Windows/macOS/Linux system tray with project status, window show/hide, refresh, explicit shutdown, close-to-tray behavior, background login startup, and per-user Windows Run, XDG autostart, or macOS LaunchAgent registration;
- added safe Git LFS storage management with custom per-repository `lfs.storage`, verified relocation, protected local-only refs/worktrees, dry-run remote checks, signed-peer/archive retention evidence, auditable cleanup and fail-closed last-copy prevention;
- added an optional cross-platform remote build agent with VPN-only firewall setup, bearer authentication, locally allowlisted recipes, isolated source snapshots without `.git` or build caches, existing synchronized workspace mode, job logs/cancellation and artifact retrieval without publishing a remote branch;
- added an Unreal Swarm over VPN workspace with Agent/Coordinator roles, project-scoped local DNS aliases, least-privilege firewall setup, safe SwarmAgent XML updates with backups, launch controls, and actionable end-to-end diagnostics for VPN, DNS, processes and TCP 8008/8009;
- added autonomous Swarm controls to the bundled CyRevisionUnreal Editor plugin, including Agent/Coordinator launch, CoordinatorRemotingHost configuration, port tests and a complete manual recovery checklist;
- added secure VPN file delivery and explicit folder sharing with a VPN-address-only listener, 256-bit project token, VPN-subnet admission, non-overwriting inbox, SHA-256 verification, transfer size limits, and traversal/symlink protection.

## 0.1.7 Alpha — 2026-08-12

- fixed macOS packaging for runtime-loaded .NET plugins by signing each Mach-O component explicitly instead of recursively treating plugin directories as native application bundles;
- added strict per-component and application-envelope signature verification before creating the portable archive and DMG.

## 0.1.6 Alpha — 2026-08-12

- integrated GitHub pull request manager with remote detection, filters, creation, draft support, files, colored patches, conversations, reviews, safe checkout, close/reopen, and confirmed merge/squash/rebase operations;
- provider-neutral pull request service boundary prepared for GitLab, Forgejo, and future decentralized review adapters, with session-only or environment-based credentials;
- complete Code workspace with hierarchical project explorer, fast `Ctrl+Shift+F` search, symbol outline, previews, and Git history for files, folders, and selected line ranges;
- optional AI workspace for Codex CLI, OpenAI-compatible APIs, Ollama, and LM Studio, with separate read, edit, network, stage, and commit permissions;
- project-scoped MCP manager with STDIO and Streamable HTTP servers, tool allow/deny policies, approval modes, timeouts, unmanaged-server isolation, and an emergency block switch;
- optional CyRevision plugin system plus separately packaged Unreal integration, autonomous Unreal Editor revision tools, authenticated loopback bridge, and guided project-plugin installation;
- optional Discord project agent using channel-scoped incoming webhooks without requiring GitHub;
- grouped commit and active-branch notifications with a persistent per-project checkpoint;
- safe first-run baseline, automatic retry after delivery failures, disabled mentions, masked webhook input, and local-only secret storage;
- desktop configuration tab, startup option, manual check, test message, bilingual offline guide, and simulated webhook tests.
- autonomous multi-project Discord sidecar with a bearer-authenticated loopback/LAN/VPN control API and a desktop control plugin;
- selectable integrated or autonomous Discord execution, including a packaged local-agent launcher and Linux user service;
- selectable system-installed or bundled WireGuard runtime per project, with SHA-256 runtime validation and no changes to unrelated tunnels.
- guided VPN initialization with LAN/gateway detection, client-only or incoming-host roles, and least-privilege firewall plans for Windows Defender, UFW and firewalld;
- router/modem port-forwarding and CGNAT checklist, with no automatic UPnP changes;
- optional Sync exchange for signed VPN invitations and responses, with payload validation and explicit rejection of private-key or secret-shaped fields.

## 0.1.5 Alpha — 2026-08-11

- new Rider-inspired code diff viewer with syntax highlighting, unified and side-by-side modes, aligned lines, and change navigation;
- modular History workspace with resizable and optional panels, three structural layouts, persistent layout preferences, and a detachable focused-diff window;
- clearer compact desktop styling with distinct panel headers and improved revision browsing;
- refined Git commit graph presentation and navigation;
- graph zoom now requires `Ctrl + mouse wheel`, while the wheel alone keeps its normal scrolling behavior;
- release-only update manager with platform-aware package selection and mandatory SHA-256 verification;
- Alpha status displayed consistently in the application and release packages.

## 0.1.0

- client desktop multi-mode Git, Sync et Backup ;
- moteur Git/Git LFS local et échanges P2P par bundles signés ;
- instance Syncthing isolée par projet et admission sécurisée des pairs ;
- snapshots dédupliqués, rétention et restauration ;
- diff textures, OBJ, texte, binaire et assets Unreal simplifiés ;
- serveur Linux, dashboard web, Docker, systemd et planificateurs ;
- pont Unreal Editor optionnel.
- assistant WireGuard indépendant, pairs VPN-only et profils Swarm/CI/services.
- graphes de commits et de relations entre fichiers, calculés localement en lecture seule ;
- interface desktop et dashboard web multilingues, avec anglais par défaut et français inclus.
- navigation des graphes par grab et zoom, ajustement automatique, disposition lisible et surbrillance des relations.
- explorateur Git interactif avec recherche, inspection de commit, comparaison A ↔ B, diff ciblé et historique par fichier ;
- Time Machine Git LFS avec inventaire local/manquant, chronologie par objet, aperçu des textures, export et restauration confirmée ;
- activité d'équipe calculée depuis Git et graphe de dépendances Unreal simplifié, tous deux hors moteur et en lecture seule ;
- planificateur de synchronisation intelligent séparant métadonnées Git, état courant, objets LFS actuels, historique et backups ;
- archive froide dédupliquée vers un stockage secondaire, sans suppression automatique du stockage actif.
- inventaires LFS signés par appareil, réplication prioritaire, demandes à la demande et transferts reprenables avec validation SHA-256 ;
- Time Machine enrichie avec les emplacements local, pair autorisé et archive froide.
- identité visuelle CyRevision intégrée au client, au serveur web et au plugin Unreal ;
- documentation locale recherchable, livrée avec l'application en anglais et en français.
- installateurs autonomes Windows, Linux et macOS, archives portables, sommes SHA-256 et workflow GitHub Releases multiplateforme reproductible.
