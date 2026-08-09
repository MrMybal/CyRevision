# Visualisations Git alternatives

L’onglet **Git graphs** propose quatre vues facultatives et strictement en lecture seule. L’analyse ne modifie ni le working tree, ni l’index, ni les commits.

## Réseau de commits

Le graphe nodal affiche les commits, leurs parents et les branches. Les couleurs distinguent les voies de l’historique et les cartes indiquent le hash court, le sujet, l’auteur et la date. Les merges apparaissent avec plusieurs connexions dès que le dépôt en contient.

La navigation est identique dans les deux vues : maintenez le bouton gauche ou central de la souris et glissez pour déplacer le plan, utilisez la molette pour zoomer sous le pointeur, et double-cliquez pour ajuster tout le graphe dans la fenêtre. Les commandes `−`, `+`, **Ajuster** et **100 %** restent disponibles en haut à droite.

## Relations entre fichiers

Cette vue mesure les fichiers modifiés dans les mêmes commits. Un lien épais signifie que les deux fichiers ont souvent évolué ensemble ; la taille d’un nœud représente son nombre de modifications. La couleur distingue le code, les assets Unreal, les textures, les modèles 3D, l’audio, les documents et la configuration.

Cliquez sur un fichier pour mettre en évidence uniquement ses voisins et leurs liens. Cliquez dans le fond pour retrouver la vue complète. Le séparateur vertical permet aussi d’agrandir le graphe en réduisant la table des fichiers actifs.

Toutes les relations sont analysées et comptées dans le résumé. Pour éviter un mur de lignes, le plan affiche au maximum les 110 relations les plus fortes parmi les fichiers visibles.

La vue aide notamment à repérer :

- les fichiers qui changent presque toujours ensemble ;
- les zones très actives ou fortement couplées ;
- les assets ou modules centraux avant une refactorisation ;
- les modifications dispersées dans un commit.

Les limites de commits et de fichiers sont réglables dans l’interface. L’option **All branches** inclut les branches locales et les références connues ; la désactiver limite l’analyse à l’historique courant. Le calcul est local et n’exécute aucun fetch.

## Activité d'équipe

La chronologie agrège les commits et le volume de lignes modifiées par jour. Les tables indiquent les contributeurs, les fichiers touchés et les points chauds. Les données proviennent uniquement des auteurs et commits déjà présents dans le dépôt : CyRevision n'installe aucun suivi utilisateur et n'envoie aucune télémétrie.

## Dépendances Unreal hors moteur

CyRevision inspecte les chaînes de références `/Game/...` détectables dans les `.uasset` et `.umap` disponibles localement. Le graphe distingue les dépendances sortantes et les références entrantes. Cette lecture simplifiée fonctionne sans lancer Unreal et reste complémentaire du futur plugin, qui pourra fournir les dépendances résolues par l'Asset Registry et les graphes Blueprint complets.
