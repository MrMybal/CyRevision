---
id: cymcp-automation
title: Assistant CyMCPSDK, CLI et MCP
description: Configurer l'assistant intégré, piloter les opérations réelles du projet et connecter des clients externes au registre commun soumis aux permissions.
section: Parcours / Code et ressources
parent: code-and-ai
order: 25
sourceRevision: 2
---

## Architecture {#architecture}
CyRevision compile les sources CyMCPSDK 0.7.0 (révision de développement `791d4d7d7680213b282ec8529d248a10f8494d77`), épinglées dans `native/cymcp.lock.json`. Modules retenus : Core, Platform, Http, MCP et AI. Local est inutile pour contacter des installations Ollama/LM Studio existantes. ChatUI exige des API composer/transcript absentes du CyUI épinglé : les interfaces Avalonia et ImGui affichent donc les événements du service Conversation. Les anciens assistants, profils MCP et options de lancement sont conservés. CyToolsCore était absent.

Le même registre lié au projet appelle les vrais services de l'application depuis le chat, le CLI et MCP. Le mode autonome couvre Git/LFS, fichiers, recherche et sauvegardes. L'instance graphique expose aussi ses parcours validés via `app.*` : PR/CI, synchronisation, VPN/Swarm, membres, fichiers/chat, builds, plugins, intégrations moteurs et préférences. Ces outils appellent les mêmes actions et services que l'interface, sans simulation séparée. Les opérations graphiques nécessitent `--instance` et n'existent pas en mode autonome.

## Configurer l'assistant {#assistant}
Activez AI Workspace dans Plugins puis Code & Assets > AI Assistant > CyMCPSDK. Listez les fournisseurs pour connaître leurs ID exacts, détectez les CLI/services locaux, puis listez les modèles et choisissez un ID précis. Une détection ne garantit pas une authentification valide.

Renseignez fournisseur, modèle, adresse/exécutable facultatifs et le **nom** d'une variable d'environnement contenant la clé API. Connect ne transmet aucun message. Send affiche progressivement la réponse ; Cancel turn arrête génération et confirmations. Une pièce jointe est une sélection explicite incorporée par le SDK, pas un droit de parcourir le disque (limites : 4 Mio par fichier, 8 Mio par message).

Les fournisseurs API/locaux et Codex reçoivent directement les outils du registre. Les autres agents CLI nécessitent un chemin MCP natif supporté : démarrez d'abord local MCP. Un fournisseur incompatible ne reçoit pas silencieusement les outils. Les agents CLI conservent leur gestion native du contexte ; le budget API ne règle pas leur compaction.

L'ID d'historique identifie une session SDK dans les données privées d'automatisation du projet, indépendamment des conversations existantes. Save session conserve l'historique complet ; Restore saved session le restaure à la connexion. Une nouvelle connexion n'écrase pas un historique existant non restauré. Déconnectez avant de changer fournisseur/session. Restart SDK runtime applique les optimisations et déconnecte fournisseurs et serveurs externes.

## Permissions et opérations {#operations}
Lecture autorisée par défaut ; modification, staging, commit et réseau restent des droits distincts désactivés. Des autorisations supplémentaires contrôlent navigation/sélection, publication/envois, opérations destructives, services, administration projet/application et builds/exécution d'agents. Toute mutation exige la validation de ses arguments exacts. Navigation, sélection et recherche de tâches demandent aussi confirmation. La fenêtre de confirmation reste disponible depuis chaque onglet pendant la génération. Une écriture demande le SHA-256 retourné par `file.read`, ou `missing` pour une création. Une copie de récupération précède le remplacement ; un état périmé est refusé. Le droit de publication ne valide jamais à lui seul un push ou un merge PR.

Les modes et plugins actifs du projet graphique s'appliquent. Changer de projet annule l'ancien tour, révoque son accès et réinitialise toutes les autorisations. Reconnectez l'assistant au nouveau périmètre avant de continuer. Le blocage MCP d'urgence s'applique aussi au SDK. Une annotation distante « lecture seule » ne donne aucun droit applicatif. Les droits sont revérifiés après confirmation, avant l'action.

En Git + Sync, le commit de l'interface peut aussi échanger les révisions : son action d'automatisation exige donc réseau, publication et services en plus de commit/staging/écriture. Modifier des fichiers déjà surveillés par un service de synchronisation indépendant conserve les effets habituels de ce service ; le droit d'écrire ne promet pas de désactiver les observateurs externes.

Familles autonomes disponibles : projet (`project.info`, `project.refresh`), état/historique/branches/remotes/diff/comparaison/conflits/graphe/worktrees Git ; staging, commit, création de branche, configuration remote, fetch/pull/push/checkout/merge ; analyse et suppression de branche locale, suppression de remote, résolution/continuation/annulation de conflit ; motifs/fichiers/historique LFS ; fichiers et recherche ; sauvegardes ; lecture et grep des ressources. Utilisez `list` puis `schema NOM` pour les noms exacts et paramètres.

Les chemins des outils fichiers/Git sont relatifs au projet choisi. Traversées, liens, données internes du dépôt et chemins de credentials courants sont exclus. L'administration applicative a un périmètre plus large : cache, stockage de sauvegarde, dossiers partagés et services peuvent désigner des emplacements hors dépôt. Vérifiez ces chemins et leurs effets exacts avant validation. Choisissez volontairement projet et fournisseur : documents, historique Git et logs CI contiennent les informations de leurs auteurs.

Le registre graphique inclut désormais création/review/commentaire/merge PR, lancement/annulation/relance CI, conflits, contrôle Sync/VPN/Swarm, rôles des pairs, configuration projet/plugins et builds. Chaque opération conserve ses droits distincts et les contrôles métier existants. `app.preferences.*` réutilise les validations et callbacks d'Edit > Preferences. `app.appearance.*` règle thème, densité et langue de l'interface native ; consultez son accusé d'application pour distinguer une demande en attente d'un changement affiché. La restauration typée d'un fichier de sauvegarde n'écrase toujours pas une destination existante.

Restent humains : permissions et validations de l'assistant, credentials des comptes, invitations privées, enrôlement d'identités/certificats, rotation de jetons, confiance dans les chemins exécutables, installation/remplacement de l'application et shell arbitraire. Aucun outil ne peut s'accorder ses propres droits. `app.capabilities` expose l'inventaire validé et les exclusions ; une nouvelle méthode applicative n'est pas automatiquement accessible. L'annulation est transmise à l'opération réelle, mais ne peut défaire une requête déjà acceptée par un serveur externe.

### Piloter l'application ouverte {#live-application}

1. Lisez `app.capabilities` puis le schéma exact de l'opération. Vérifiez mode, plugin et droits nécessaires.
2. Lisez `app.context` avec l'ID d'espace, par exemple `pullrequests`, `ci` ou `plugins`. Attendez si `busy` est vrai.
3. Lisez les clés stables avec `app.table.read`, puis préparez la sélection via `app.table.select` ou `app.table.setFlag`. Modifiez les brouillons et options enum via les outils nommés `app.fields.set.*`. Modifier ne sauvegarde ni ne publie implicitement.
4. Relisez le contexte après chaque modification/sélection. Transmettez ses `stateToken` et `target` exacts à `app.<espace>.<NomAction>`. Validation à usage unique ; `StaleState` impose un nouveau contexte et une nouvelle confirmation si la sélection, le brouillon ou le périmètre a changé.
5. Relisez contexte/table pour le résultat. Un échec ou une annulation métier devient une erreur d'outil, pas un faux succès.

`app.tasks.providers` liste les connecteurs de tâches actifs. `app.tasks.search` réutilise leur connexion projet déjà configurée ; ses résultats sont dans `NativeWorkItemSearch`. `app.tasks.link` insère les clés exactes dans un brouillon : utilisez un contexte frais `pullrequests` pour une PR, ou `changes` pour un commit. Terminer les tâches liées après merge reste une action de publication séparée. Le modèle ne fournit ni réglages de connexion ni jetons.

## CLI {#cli}
Le compagnon est `Cli/CyRevision.Cli.exe` sous Windows (`Cli/CyRevision.Cli` sous Linux/macOS). Les exécutables graphiques/agents gardent leurs options. Depuis la racine du dépôt :
```powershell
./artifacts/native-v2/Cli/CyRevision.Cli.exe --help
./artifacts/native-v2/Cli/CyRevision.Cli.exe list --project .
./artifacts/native-v2/Cli/CyRevision.Cli.exe schema file.write --project .
./artifacts/native-v2/Cli/CyRevision.Cli.exe invoke project.info --project .
./artifacts/native-v2/Cli/CyRevision.Cli.exe invoke file.read --project . --arguments '{"path":"README.md"}'
```
`--arguments-file` évite les différences d'échappement JSON des shells ; `--profile-dir` choisit les données d'automatisation, `--backup-store` le stockage de sauvegarde. Une mutation directe exige les droits `--allow-write`, `--allow-stage`, `--allow-commit`, `--allow-network` pertinents et `--confirm`. Droits supplémentaires : `--allow-navigation`, `--allow-publication`, `--allow-destructive`, `--allow-services`, `--allow-administration`, `--allow-builds`. Exemple : push autonome exige écriture + réseau + publication + confirmation ; pull exige aussi commit, puisqu'il peut créer un commit de merge. Pour chat/serveur, `--approval-file` contient un accord exact à usage unique `{ "operation": "file.write", "arguments": { ... } }`. `--confirm` ne valide jamais tout un chat/serveur. Un appel attaché utilise les permissions et confirmations de l'application ; les flags CLI ne peuvent pas les remplacer.

Sortie JSON, diagnostics stderr ; `--stream` produit des événements JSONL puis un résultat final. Codes : 0 succès, 2 arguments, 3 refus, 4 confirmation requise, 5 runtime manquant, 6 erreur métier/protocole, 130 annulation. `--timeout-ms` borne la durée. Commandes `providers`, `discover`, `chat --provider ID --model ID --text TEXTE` ; options `--endpoint`, `--api-key-env`, `--executable`, `--attachment`, `--history-id`, `--restore-history`. Sans ID explicite, le chat crée une nouvelle session.

## Connexions MCP {#mcp}
STDIO autonome : configurez l'exécutable CLI et les arguments `mcp --project DOSSIER_PROJET --profile-dir DOSSIER_PRIVE`. stdout est réservé à JSON-RPC. Pas d'interface ouverte. Initialisation, découverte/appels et ressources utilisent l'implémentation SDK.

Instance graphique : Start local MCP affiche le chemin d'un fichier privé contenant jeton bearer et URL loopback authentifiée. Le client MCP contacte cette URL suivie de `/mcp`, avec Authorization: Bearer et le jeton lu localement. Ne copiez pas ce jeton dans un fichier suivi. Health indique uniquement la disponibilité du SDK ; les opérations métier exigent l'authentification. Stop local MCP arrête le serveur et retire son descripteur.
```powershell
./artifacts/native-v2/Cli/CyRevision.Cli.exe list --instance FICHIER_INSTANCE_PRIVE
./artifacts/native-v2/Cli/CyRevision.Cli.exe invoke app.state --instance FICHIER_INSTANCE_PRIVE
./artifacts/native-v2/Cli/CyRevision.Cli.exe serve --project . --endpoint-file FICHIER_INSTANCE_PRIVE
```
La dernière commande crée un serveur autonome, pas une connexion à l'interface ouverte. Gardez le processus actif ; Ctrl+C l'arrête. Une terminaison forcée peut laisser un descripteur périmé, sans rejeu d'opération. Les transports SDK proposent STDIO et HTTP/MCP local authentifié, avec comportements SSE/abonnements finis. Tous les clients tiers ne gèrent pas notifications ou reprise de flux : TTL et actualisation explicite restent disponibles.

## Outils externes {#remote-tools}
Réutilisez le profil sélectionné dans l'onglet MCP. Activez projet MCP, serveur, outils externes et les droits écriture/réseau pertinents. Indiquez des noms exacts autorisés ; la liste interdite prévaut. Connect selected MCP profile enregistre uniquement cette liste avec des noms stables `remote_…`. Chaque appel distant exige confirmation, même en lecture : règle plus stricte que l'approbation automatique historique. Désactiver profil/outil révoque le transfert ; redémarrez le SDK pour remplacer une connexion.

STDIO : arguments un par ligne, dossier et environnement configurés. HTTP : nom de variable bearer. Les délais configurés de démarrage/appel bornent les requêtes SDK. En-têtes HTTP personnalisés et authentification OAuth/ChatGPT ne sont pas implémentés dans cet adaptateur. Les intégrations précédentes subsistent ; authentification/en-têtes non supportés sont refusés. Tous les appels demandent confirmation plutôt que de reprendre les exceptions d'approbation automatique. Une erreur distante arrive à l'assistant comme erreur d'outil.

Le chat autonome accepte `--remote-config-file FICHIER_JSON_PRIVE` : `id`, `url` ou `executable`/`arguments`/`cwd`/`environment`, `allowedTools`, éventuellement `tokenEnvironment`, `cacheTtlMs`. Il s'agit d'une configuration de l'hôte, pas d'une adresse choisie par le modèle. Accordez l'opération qualifiée exacte par `--approval-file`.

## Optimisations indépendantes {#optimizations}
Découverte compacte par défaut : cinq outils recherchent les commandes, décrivent un schéma, appellent l'opération, lisent et recherchent les ressources. Découverte complète avec `--full-discovery`. Le registre et ses droits restent identiques.

Les lectures bornées créent aussi des snapshots immuables pour grep (16 maximum, 1 Mio chacun), invalidés après mutation/actualisation explicite. `code.search` utilise ripgrep ou le service managé dans le seul projet sélectionné. Lectures de fichiers par plages d'octets avec base64, listes structurées avec offset/limit.

Les gros résultats retournent une ressource privée conservée temporairement. Lisez son manifeste puis `firstUri`/`nextUri` ; ne relancez jamais la mutation pour récupérer la suite. Défauts : mémoire 64 Mio, morceaux 32 Kio, rétention 300 secondes. Spool disque facultatif : quota 1 Gio dans les données privées d'automatisation. Quotas mémoire/disque, morceaux, TTL et emplacement du profil/cache sont configurables séparément. Ces résultats sont temporaires ; checkpoints et copies de récupération ont un stockage distinct.

Cache découverte/lecture : TTL 30 secondes, 0 le désactive. Notifications et mesures JSONL de métadonnées sont deux options séparées. `project.refresh` invalide snapshots et caches externes ; `app.refresh` actualise aussi l'interface. Pas de cache/rejeu des mutations. Budget de contexte API : 0 illimité ; une limite conserve l'historique sauvegardé intégral, l'instruction système et le groupe d'outils actif complet, en écartant seulement l'ancien contexte des requêtes API. Un groupe actif trop grand échoue sans suppression de résultat. Aucun résumé inventé.

## Compilation et vérification {#build}
Windows : `powershell -NoProfile -File scripts/build-native-v2.ps1` compile le SDK épinglé et publie interface + CLI. SDK seul : `scripts/build-cymcp.ps1`. Linux/macOS : `bash scripts/build-cymcp.sh`, CMake 3.24+, C++20 et développement libcurl sur l'architecture cible. Les scripts de release incluent CLI/runtime. En compilation croisée, fournissez le runtime de la bonne cible via `CyMcpNativeRuntimeDirectory`, jamais la DLL de l'hôte.
```text
python scripts/verify-cymcp-snapshot.py
python scripts/test_cymcp_integration.py --cli artifacts/native-v2/Cli/CyRevision.Cli.exe
python scripts/test_cymcp_desktop.py --app artifacts/native-v2/CyRevision.Desktop.exe --cli artifacts/native-v2/Cli/CyRevision.Cli.exe
python scripts/test_cymcp_full_app.py --app artifacts/native-v2/CyRevision.Desktop.exe --cli artifacts/native-v2/Cli/CyRevision.Cli.exe
python scripts/test_cymcp_git_actions.py --cli artifacts/native-v2/Cli/CyRevision.Cli.exe
dotnet test CyRevision.sln -c Release
```
Ces tests utilisent les vrais services, des dépôts Git jetables, un remote bare local et des serveurs modèle/API GitHub simulés localement. Ils exercent checkout, merge/récupération de conflit, commentaire/merge PR, annulation CI, préférences, cibles périmées et révocation des droits. Des tests classent chaque action UI publique comme validée ou réservée à l'hôte. Ils ne certifient pas les comptes réels, la CI de production, les changements VPN/pare-feu de l'OS ni chaque installation externe de plugin. Plateforme exercée : Windows x64 ; Linux/macOS restent à tester sur leurs plateformes.

[Parcours IA existants](doc:code-and-ai) · [Politique MCP existante](doc:mcp-policy)
