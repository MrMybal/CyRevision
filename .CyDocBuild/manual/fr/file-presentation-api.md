---
id: file-presentation-api
title: Fournisseurs d’aperçu et de comparaison
description: Retourner du texte, des images ou des métadonnées indépendants de l’interface.
section: Développeurs / API publique
parent: plugin-api
kind: api
order: 150
sourceRevision: 1
---

## Demandes et résultats {#requests-and-results}

Implémentez `IFilePresentationProvider`. `ProviderId` identifie le fournisseur et `Priority` règle son ordre. `FilePreviewRequest` contient racine, chemins relatif/absolu et taille du fichier. `FileDiffRequest` contient racine, chemin relatif et chemins des versions référence/candidate.

Vérifiez `CanPreview` ou `CanCompare` avant de créer le résultat. `FilePresentationResult` décrit une présentation `Text`, `Image` ou `Metadata`, un résumé et des texte, image, métadonnées ou chemins d’images référence/candidate/différence facultatifs. Retournez null si aucune présentation ne peut être fournie.

## Décrire les capacités réelles {#capabilities}

Un aperçu texte n’est pas un analyseur sémantique. Une empreinte de paquet binaire n’est pas une scène rendue. Un pointeur Git LFS n’est pas la ressource complète. Ne promettez pas un rendu ou une comparaison que le fournisseur ne produit pas réellement.

[Aperçus des fichiers](doc:asset-diff)
