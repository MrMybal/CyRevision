---
id: tasks
title: Workflow CyTask, Jira et ClickUp
description: Configurer les fournisseurs et vérifier les transitions après fusion.
section: Extensions
order: 42
sourceRevision: 1
---

## Activer le fournisseur {#enable-the-provider}
Dans Plugins, trouvez CyTask, Jira ou ClickUp. Activez/configurez seulement l'intégration installée nécessaire. Les panneaux/modes dépendent des capacités du plugin ; son installation ne connecte pas automatiquement tous les projets.

## Tâches liées aux PR {#linked-tasks-in-pull-requests}
PR > Actions peut détecter URL/ID supportés dans le titre, la description ou la branche. Vérifiez tâche, fournisseur et projet avant une transition. Un ID ambigu peut exiger un mapping explicite fournisseur/projet.

## Après fusion {#after-merge}
1. Examinez les références détectées et la transition voulue.
2. Préférez la confirmation manuelle pour établir le workflow.
3. N'activez une politique automatique qu'avec un mapping projet/fournisseur/statut vérifié.
4. Fusionnez avec les droits normaux du dépôt.
5. Contrôlez séparément le résultat de chaque mise à jour de tâche.

Une PR fusionnée et une transition réussie sont deux opérations distantes. L'une peut réussir et l'autre échouer : droits, réseau, tâche absente ou transition invalide. Vérifiez/actualisez la tâche avant de réessayer ; ne refusionnez pas pour déclencher une transition.

Il n'existe pas de statut Done universel : les workflows Jira/ClickUp varient. Les données et statuts propres à CyTask peuvent différer des deux.

```image
{"id": "tasks", "src": "assets/tasks.fr.svg", "alt": "Workflow CyTask, Jira et ClickUp", "controls": "minimal"}
```

[Voir aussi](doc:pull-request-manager)
