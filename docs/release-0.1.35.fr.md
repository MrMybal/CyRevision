# CyRevision 0.1.35 Alpha

[English](https://github.com/MrMybal/CyRevision/blob/v0.1.35/docs/release-0.1.35.md) · [Release et téléchargements](https://github.com/MrMybal/CyRevision/releases/tag/v0.1.35)

## Changements

- Ajout de l'assistant CyMCPSDK : découverte des fournisseurs/modèles, réponses en streaming, annulation, pièces jointes et sauvegarde des conversations.
- Partage des vraies opérations projet entre l'assistant, `Cli/CyRevision.Cli` et les clients MCP locaux authentifiés.
- Connexion de 264 actions de bureau validées : Git/LFS, PR/CI, Sync/VPN/Swarm, sauvegardes, tâches, plugins, builds et préférences.
- Droits indépendants pour navigation, publication, opérations destructives, services, administration et builds. Validation des cibles et arguments exacts dans une fenêtre de confirmation globale.
- Refus des sélections/brouillons périmés, remontée des erreurs métier et conservation des credentials et de la configuration des exécutables sous le contrôle de l'hôte.
- Recherche de tâches et insertion de leurs liens dans les brouillons PR/commit ; automatisation de checkout, push/pull, merge et récupération des conflits Git.
- Mise à jour du manuel CyDoc intégré : 87 pages par langue, en anglais et français, avec configuration de l'assistant, exemples CLI/MCP et limites opérationnelles.

## Paquets et prérequis

Cette release fournit les paquets **Windows x64** : installateur, ZIP portable et sommes SHA-256. Native V2 (`CyRevision.Native.exe`) et Classic (`CyRevision.Desktop.exe`) sont inclus, avec le CLI dans `Cli`. Les paquets sont autonomes ; Git/Git LFS et les outils optionnels des fournisseurs ou services gardent leurs propres prérequis.

Les sources Linux et macOS sont conservées, mais leurs derniers paquets publiés restent ceux de la [0.1.34](https://github.com/MrMybal/CyRevision/releases/tag/v0.1.34). Ils ne sont ni recompilés ni testés pour cette release.

Le lecteur HTML actualisé est inclus dans `Documentation/Documentation.html`, avec tout son dossier `DocData`. Ouvrez Aide > Guide utilisateur ou F1. La documentation en ligne se déploie indépendamment des paquets applicatifs.

## Limites documentées

- Native V2, Sync, Git + Sync, Lore, sauvegardes et plugins restent en Alpha ; les fonctions Git sont en Bêta. Conservez des sauvegardes indépendantes et validez la restauration avant la production.
- Native V2 nécessite Windows 10/11 x64 et Direct3D 11. Les paquets Windows ne sont pas signés ; le système peut afficher des avertissements de confiance.
- Les opérations `app.*` nécessitent un projet ouvert et les droits/confirmations de l'hôte. Le mode autonome Git/LFS/fichiers/sauvegardes n'expose pas tous les services graphiques.
- Credentials, enrôlement d'identités, permissions de l'assistant, shell arbitraire et installation d'exécutables restent des parcours contrôlés par l'hôte.
- Comptes fournisseurs, CI réelle et changements VPN/pare-feu de l'OS nécessitent configuration et validation dans leur environnement. Les scénarios automatisés PR/CI/tâches utilisent des API locales de test.
- L'annulation ne peut pas défaire une requête déjà acceptée par un service externe.

[Installation](https://github.com/MrMybal/CyRevision/blob/v0.1.35/README.fr.md#installation) · [Suivi des problèmes](https://github.com/MrMybal/CyRevision/issues)
