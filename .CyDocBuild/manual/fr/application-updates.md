---
id: application-updates
title: Mises à jour des releases stables
description: Consulter, télécharger, vérifier et installer une release CyRevision depuis un popup intégré.
section: Bien démarrer
parent: getting-started
order: 45
sourceRevision: 2
---

## Releases uniquement {#releases-only}
Le gestionnaire consulte uniquement la dernière GitHub Release stable de CyRevision. Les commits, branches, brouillons et préversions ne sont jamais proposés comme mises à jour. Les dépôts des projets restent indépendants et ne nécessitent pas GitHub.

## Popup de mise à jour {#update-prompt}
CyRevision vérifie après le démarrage et affiche un popup lorsqu'une version stable plus récente existe. Le popup présente les notes de version et propose Plus tard, Voir la release et Télécharger et installer. Le bouton Vérifier les mises à jour reste disponible à tout moment.

## Paquet de la plateforme {#platform-package}
CyRevision sélectionne l'installateur natif : Setup Windows, paquet DEB Linux x64 ou ARM64, ou image DMG macOS x64 ou ARM64. La page de la release reste accessible si aucun paquet compatible n'est présent.

## Installation sûre {#safe-installation}
Le paquet est enregistré dans le cache CyRevision et son SHA-256 doit correspondre au manifeste SHA256SUMS.txt. CyRevision programme ensuite un lanceur externe, arrête ses services en arrière-plan et se ferme complètement. Le lanceur attend la fin du processus avant d'ouvrir l'installateur. Aucun git pull du code source n'est effectué.

```image
{"id":"updates","src":"assets/updates.fr.svg","alt":"Mises à jour des releases stables","controls":"minimal"}
```

[Voir aussi](doc:tray-and-startup)
