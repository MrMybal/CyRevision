---
id: cystore
title: Versions de paquets CyStore
description: Examiner les versions publiées et leurs manifestes.
section: Extensions
parent: plugins
order: 41
sourceRevision: 2
---

## Ouvrir CyStore {#open-cystore}
Activez le fournisseur CyStore et sa vue Alpha pour un projet compatible. Configurez les options endpoint/projet proposées puis actualisez les versions. Les versions du dépôt et les releases de mise à jour de l'app sont deux catalogues distincts.

## Examiner une version {#inspect-a-version}
1. Sélectionnez la version et attendez manifeste/fichiers.
2. Lisez noms, tailles et métadonnées avant de demander le contenu.
3. Choisissez la destination d'un téléchargement/export proposé.
4. Vérifiez la fin de l'opération : sélectionner une ligne n'installe rien.

La liste est un manifeste, pas nécessairement une copie locale. La disponibilité dépend du store configuré et des droits du compte. Aucun résultat peut signifier absence de version, filtre, connexion ou droits insuffisants : lisez l'état avant de réessayer.

## Périmètre et limites {#scope-and-limits}
CyStore est en Alpha. Utilisez les opérations réellement exposées par le fournisseur installé ; ce n'est pas un client universel de marketplaces. N'écrasez pas un plugin moteur fonctionnel au seul motif d'un numéro supérieur. Vérifiez compatibilité, conservez l'ancienne version et testez le projet consommateur.

Pour mettre à jour CyRevision lui-même, utilisez Help > Check for updates.

```image
{"id":"integrations","src":"assets/integrations.fr.svg","alt":"Versions de paquets CyStore","controls":"minimal"}
```

[Voir aussi](doc:plugins)
