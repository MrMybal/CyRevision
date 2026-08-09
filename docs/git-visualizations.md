# Visualisations Git alternatives

L’onglet **Git graphs** propose deux vues facultatives et strictement en lecture seule. L’analyse ne modifie ni le working tree, ni l’index, ni les commits.

## Réseau de commits

Le graphe nodal affiche les commits, leurs parents et les branches. Les couleurs distinguent les voies de l’historique et les cartes indiquent le hash court, le sujet, l’auteur et la date. Les merges apparaissent avec plusieurs connexions dès que le dépôt en contient.

## Relations entre fichiers

Cette vue mesure les fichiers modifiés dans les mêmes commits. Un lien épais signifie que les deux fichiers ont souvent évolué ensemble ; la taille d’un nœud représente son nombre de modifications. La couleur distingue le code, les assets Unreal, les textures, les modèles 3D, l’audio, les documents et la configuration.

La vue aide notamment à repérer :

- les fichiers qui changent presque toujours ensemble ;
- les zones très actives ou fortement couplées ;
- les assets ou modules centraux avant une refactorisation ;
- les modifications dispersées dans un commit.

Les limites de commits et de fichiers sont réglables dans l’interface. L’option **All branches** inclut les branches locales et les références connues ; la désactiver limite l’analyse à l’historique courant. Le calcul est local et n’exécute aucun fetch.
