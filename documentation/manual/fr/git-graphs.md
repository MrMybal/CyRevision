---
id: git-graphs
title: Réseau de commits et graphes
description: Explorer la topologie et les relations dans un échantillon.
section: Git
order: 15
sourceRevision: 1
---

## Analyser {#analyze}
Git > Git graphs : choisissez limites de commits/fichiers et inclusion des branches puis Analyze repository. Commencez modestement sur un gros dépôt pour borner mémoire et travail.

## Interpréter {#interpret-the-views}
Commit network montre ascendance et voies de fusion. File relations associe les fichiers modifiés ensemble dans l'échantillon, pas nécessairement leurs dépendances à l'exécution. Git activity décrit cet échantillon, pas la productivité individuelle. Les dépendances Unreal dépendent des métadonnées et de l'analyseur, pas d'un chargement moteur complet.

## Naviguer {#navigate}
Glissez pour déplacer. Molette : vertical ; Maj+molette : horizontal ; Ctrl+molette : zoom là où indiqué. Fit ou double-clic recentre. Sélectionnez un nœud/une ligne pour les métadonnées du commit. Agrandissez une fenêtre trop étroite.

L'analyse locale est en lecture seule : aucun fetch, changement d'index ni réécriture d'historique. Si le graphe paraît vide, vérifiez compteur, branches et filtres puis Fit/réanalysez. Un dépôt sans commit n'a pas de topologie à montrer.

```image
{"id": "git", "src": "assets/git.fr.svg", "alt": "Réseau de commits et graphes", "controls": "minimal"}
```

[Voir aussi](doc:git-history)
