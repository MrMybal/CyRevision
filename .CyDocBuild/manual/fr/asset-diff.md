---
id: asset-diff
title: Différences d'images et d'assets binaires
description: Choisir une comparaison texte, image ou métadonnées selon le fichier.
section: Parcours / Code et ressources
parent: code-explorer
order: 22
sourceRevision: 2
---

## Choisir une comparaison {#choose-a-comparison}
Ouvrez Code & Assets > Asset preview & diff ou un fichier modifié dans Changes/History. Choisissez référence et candidat si proposé. Unified et Side by side comparent du texte ; Images / Heatmap compare les images rendues supportées.

## Interpréter {#interpret-correctly}
Ajouts/suppressions montrent des lignes, pas toute leur sémantique. JSON/INI restent du texte même généré. Utilisez le défilement horizontal ; un aperçu borné indique qu'il est incomplet.

Pour les images, comparez versions, dimensions et carte de différences. Zoom/déplacement/ajustement aident à examiner les détails. La heatmap mesure des pixels, pas la pertinence artistique. Gardez les dimensions originales en tête.

## Unreal et LFS {#unreal-and-lfs}
L'aperçu hors ligne .uasset/.umap présente métadonnées et noms détectés, pas une scène rendue complète. Ouvrez l'éditeur propriétaire pour un rendu fidèle. Un pointeur LFS est un petit texte décrivant un objet, pas son image ou son maillage. Récupérez explicitement les contenus manquants.

Open diff in window ou Enlarge garde la comparaison à côté d'un outil. L'aperçu est en lecture seule ; restauration/export sont distincts. Un aperçu indisponible ne prouve pas qu'un asset est corrompu : vérifiez dans son application source.

```image
{"id":"split-diff-v2","src":"assets/split-diff-v2.png","alt":"Différences d'images et d'assets binaires","controls":"minimal"}
```

[Voir aussi](doc:lfs-time-machine)
