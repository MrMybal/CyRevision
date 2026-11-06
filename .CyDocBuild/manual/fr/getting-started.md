---
id: getting-started
title: Bien démarrer
description: Choisir un mode de projet, enregistrer un dossier et comprendre les outils facultatifs.
section: Bien démarrer
order: 0
sourceRevision: 2
---

CyRevision garde Git, la synchronisation, les sauvegardes et le VPN indépendants. Activez uniquement les modules nécessaires au projet.

## Créer un dépôt {#create-a-repository}
Utilisez Créer un dépôt pour un nouveau projet Git, ou Ouvrir un dépôt pour un dépôt existant. Ajouter un dossier enregistre un dossier pour Sync ou Backup sans imposer Git.

## Modes de projet {#project-modes}
• Git : gestion locale des révisions uniquement.
• Git + Sync : Git local et échange signé entre pairs.
• Sync : fichiers courants synchronisés sans Git.
• Sync + versions : synchronisation avec versioning Syncthing.
• Backup : snapshots dédupliqués sans synchronisation continue.

## Prérequis {#requirements}
Git et Git LFS sont requis pour les projets Git. Syncthing est nécessaire uniquement pour un mode Sync. WireGuard est nécessaire uniquement pour le VPN. Ouvrir un projet ne lance jamais implicitement un outil facultatif.

## V2 native et Classic {#native-v2-and-classic}
V2 utilise CyUI natif sous Windows. Le moteur géré conserve les mêmes services projet ; Classic reste accessible dans View > Classic interface et constitue l'interface multiplateforme. Les deux interfaces ouvrent le même lecteur CyDoc local. L'anglais est la langue par défaut ; Français se choisit dans Edit > Language ou le sélecteur du guide.

## Première session {#first-session}
1. Ouvrez, clonez, créez un dépôt ou ajoutez un dossier selon le besoin.
2. Choisissez le mode dans Overview > Project.
3. Vérifiez projet/branche dans l'en-tête avant toute opération.
4. Examinez les changements avant commit ; configurez un remote seulement pour publier.
5. Préparez une sauvegarde indépendante et testez la récupération avant de compter sur la synchronisation.

Les captures utilisent un projet de démonstration et peuvent montrer l'interface anglaise ou un autre thème. Légendes et schémas suivent la langue du guide. Les chapitres associés détaillent les procédures.

```image
{"id":"modes","src":"assets/modes.fr.svg","alt":"Bien démarrer","controls":"minimal"}
```

```image
{"id":"workspace-v2","src":"assets/workspace-v2.png","alt":"Bien démarrer","controls":"minimal"}
```

Consultez les vues d’exemple ci-dessous pour repérer les projets, l’historique et la comparaison. Les captures montrent l’interface anglaise ; les explications et le manuel sont disponibles en français.

## Visite de l’interface {#interface-tour}

```gallery
{"id":"interface-tour","title":"Repères dans l’interface","controls":"minimal","items":[{"type":"image","src":"assets/workspace-v2.png","alt":"Projets, changements et aperçu de fichier","title":"Préparer les changements","description":"La liste des projets reste à gauche ; les outils du projet actif sont au centre."},{"type":"image","src":"assets/history-v2.png","alt":"Révisions, fichiers du commit et comparaison","title":"Explorer l’historique","description":"Choisissez une révision, puis un fichier, pour examiner sa différence."},{"type":"image","src":"assets/split-diff-v2.png","alt":"Comparaison de deux versions côte à côte","title":"Comparer deux versions","description":"Les deux panneaux suivent le même fichier dans les révisions choisies."}]}
```

[Voir aussi](doc:navigation)
