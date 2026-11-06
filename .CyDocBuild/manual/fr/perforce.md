---
id: perforce
title: Espace Perforce
description: Examiner les changelists et réconcilier avec les droits serveur.
section: Extensions
parent: plugins
order: 40
sourceRevision: 2
---

## Se connecter volontairement {#connect-deliberately}
Activez Perforce, sélectionnez le projet et configurez serveur/client/utilisateur avec les réglages proposés. Vérifiez que le client pointe sur les bons fichiers locaux. Perforce et Git sont distincts ; un remote Git n'est pas une connexion Perforce.

## Examiner avant d'agir {#inspect-before-action}
Actualisez fichiers ouverts, changelists en attente et historique. Sélectionnez un chemin pour lire son contexte dépôt/workspace. Authentification, mapping client et protections serveur déterminent les actions.

## Sync, reconcile et submit {#sync-reconcile-and-submit}
1. Préservez buffers d'éditeur et travail local.
2. Prévisualisez le périmètre sync/reconcile.
3. Ne confirmez que les ajouts/modifications/suppressions attendus.
4. Vérifiez changelist et description avant Submit.
5. Contrôlez le résultat serveur et actualisez.

Reconcile et Submit ne sont pas des diagnostics en lecture seule. Évitez une réconciliation large sur caches et sorties générées. Submit modifie le dépôt et peut lancer une automatisation. Résolvez les conflits serveur avec Perforce, pas les contrôles Git.

En cas d'échec, contrôlez racine du client, connexion/authentification, exécutable p4 et réponse serveur. Modifier des permissions de dossiers sans rapport ne remplace pas un mapping valide.

```image
{"id":"integrations","src":"assets/integrations.fr.svg","alt":"Espace Perforce","controls":"minimal"}
```

[Voir aussi](doc:plugins)
