# Diff d’assets sans Unreal Editor

| Format | Analyse actuelle |
| --- | --- |
| texte/code/config | lignes ajoutées et retirées |
| PNG/JPEG/BMP/GIF/WebP | dimensions, pixels modifiés, écart moyen, heatmap |
| OBJ | sommets, faces, UV, normales, bounds et superposition filaire 3D |
| `.uasset` / `.umap` | signature package, taille, blocs modifiés, symboles et types probables |
| autres binaires | SHA-256 et blocs différents |

L’analyse Unreal externe est volontairement prudente : les formats sérialisés varient selon la version du moteur et les plugins. CyRevision ne prétend pas reconstruire un graphe Blueprint exact sans les sérialiseurs d’Unreal. Le pont Unreal optionnel pourra fournir plus tard les nœuds, propriétés et thumbnails exportés par le moteur, tout en laissant le comparateur principal utilisable moteur fermé.
