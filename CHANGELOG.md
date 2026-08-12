# Changelog

## Unreleased

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
