---
id: global-search
title: Recherche de code et historique des lignes
description: Rechercher du texte avec un périmètre explicite.
section: Code et assets
order: 21
sourceRevision: 1
---

## Ouvrir et délimiter {#open-and-scope}
Ctrl+Maj+F ou Code & Assets > Code search. Saisissez le texte et limitez les fichiers avec des motifs séparés par des points-virgules, par exemple *.cs;*.cpp. Activez casse, mots entiers ou expressions régulières selon le besoin.

## Suivre un résultat {#follow-a-result}
1. Lancez la recherche et attendez sa fin.
2. Sélectionnez un résultat pour ouvrir le fichier et la ligne.
3. Lisez le contexte, pas seulement l'extrait trouvé.
4. Sélectionnez une plage pour History of selection si proposé ; utilisez l'historique de fichier pour l'ensemble.

ripgrep est utilisé si disponible, avec un repli géré. Les règles d'indexation peuvent exclure les dossiers générés/dépendances. Aucun résultat signifie aucun match dans le périmètre recherché, pas forcément aucun octet sur tout le disque. Vérifiez motifs, casse, fichiers masqués et exclusions.

## Expressions régulières {#regular-expressions}
Commencez par une recherche littérale. Corrigez une expression invalide au lieu de la relancer. La recherche est en lecture seule ; remplacer/modifier est une opération séparée à examiner avant sauvegarde. Après changement de branche ou édition externe, actualisez l'index si les résultats sont anciens.

```image
{"id": "workspace-v2", "src": "assets/workspace-v2.png", "alt": "Recherche de code et historique des lignes", "controls": "minimal"}
```

[Voir aussi](doc:code-explorer)
