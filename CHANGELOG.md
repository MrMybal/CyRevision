# Changelog

## Unreleased

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
