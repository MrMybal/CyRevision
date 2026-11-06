---
id: unreal-plugin
title: Extension CyRevision optionnelle et plugin Unreal autonome
description: Installer CyRevisionUnreal depuis l'application, gérer les révisions dans Unreal et connecter les deux côtés au besoin.
section: Extensions
parent: plugins
order: 37
sourceRevision: 2
---

## Deux plugins séparés {#two-separate-plugins}
Unreal Engine Integration est une extension CyRevision optionnelle, désactivée par défaut. CyRevisionUnreal est un plugin Editor autonome inclus dans chaque package desktop.

## Compatibilité {#compatibility}
Le plugin source prend en charge Unreal 4.27 et 5.0 à 5.8 pour les projets C++. Le package Windows embarque aussi les binaires Win64 exacts pour les projets Blueprint-only sous Unreal 5.2 à 5.8. CyRevision détecte le moteur et le type de projet, affiche les deux listes de compatibilité et bloque toute installation dangereuse ou indisponible.

## Installation depuis cyrevision {#install-from-cyrevision}
Ouvrez Plugins, activez Unreal Engine Integration, sélectionnez un fichier .uproject puis choisissez Install / update in project. Les anciens fichiers du plugin sont déplacés vers Saved/CyRevision/PluginBackups avant une mise à jour. Les projets C++ compilent les sources avec leur cible Editor ; les projets Blueprint-only reçoivent une variante précompilée exacte sans conversion du projet en C++.

## Outils de révision autonomes {#autonomous-revision-tools}
Tools > Revision dashboard fonctionne sans CyRevision. Il affiche l'état Git et les commits récents, puis permet stage, commit, fetch, pull et push.

## Connexion directe {#direct-connection}
CyRevision crée un jeton aléatoire sous Saved/CyRevision/bridge.json et écoute uniquement en loopback. Unreal peut prévenir le projet CyRevision correspondant après une opération Git ou de présence. Désactiver l'extension CyRevision arrête le pont sans retirer le plugin Editor.

## Réservations souples {#advisory-reservations}
Les marqueurs du Content Browser restent informatifs : aucun checkout, changement de permission ou verrou Git LFS n'est créé.

```image
{"id":"integrations","src":"assets/integrations.fr.svg","alt":"Extension CyRevision optionnelle et plugin Unreal autonome","controls":"minimal"}
```

[Voir aussi](doc:unreal-builds)
