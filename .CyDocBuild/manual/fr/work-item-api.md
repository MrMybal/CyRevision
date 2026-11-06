---
id: work-item-api
title: Contrats de suivi des tâches
description: Connecter des fournisseurs Jira, ClickUp ou CyTask par projet et appliquer des transitions explicites.
section: Développeurs / API publique
parent: plugin-api
kind: api
order: 150
sourceRevision: 1
---

## Connexion {#connection}

Implémentez `IWorkItemIntegrationPlugin` dans `CyRevision.Plugin.Abstractions`. Il respecte aussi le cycle de vie des plugins. `Provider` expose les noms et libellés de configuration. `WorkItemConnectionSettings` contient `ProjectId`, `BaseUrl`, `ScopeId`, `AccountName` et `TokenEnvironmentVariable` ; il n’a volontairement aucune propriété de jeton.

Transmettez les identifiants de session uniquement à l’opération qui en a besoin. Recherche et résolution retournent des `WorkItemReference` : identité du fournisseur, identifiant/clé de tâche, titre, état et URL.

## Lire, puis changer d’état {#transitions}

1. Chargez et testez la connexion du projet/fournisseur.
2. Recherchez ou résolvez la tâche voulue.
3. Lisez les transitions proposées par son workflow actuel.
4. Appliquez la transition choisie seulement après la décision configurée/manuelle de l’hôte.
5. Examinez les états précédent et nouveau dans le résultat.

Une fusion de PR réussie ne prouve pas la réussite de la transition de tâche. Les erreurs de droits, de workflow et de réseau sont distinctes. N’inventez pas de nom universel d’état terminé.

[Parcours des tâches](doc:tasks)
