---
id: project-mode-api
title: Modes de projet fournis par un plugin
description: Déclarer capacités, rétention et onglets sans couplage avec l’interface de bureau.
section: Développeurs / API publique
parent: plugin-api
kind: api
order: 150
sourceRevision: 1
---

## Déclarer les modes {#declare-modes}

Implémentez `IProjectModeProvider` et exposez `ProjectModes`. Chaque `PluginProjectModeDescriptor` possède un identifiant, un nom, une description, des capacités, une rétention, les identifiants des onglets et un libellé de catégorie. Les identifiants de mode sont uniques dans le fournisseur.

`PluginProjectModeFeatures` déclare les capacités Git, LFS, synchronisation entre pairs, sauvegarde et remote standard. `PluginProjectModeRetention` déclare une rétention de l’état courant, chronologique ou permanente et des limites facultatives de versions, durée et stockage.

## Évaluer la compatibilité {#compatibility}

L’hôte transmet à `EvaluateProjectMode` l’identité, le nom, la racine et les plugins activés du projet sélectionné. Retournez un indicateur de disponibilité et une explication utile. Cette vérification ne change pas le mode et ne modifie pas le projet. Le fournisseur doit être activé pour ce projet avant que ses modes et espaces enregistrés soient proposés.

[Choisir un mode](doc:project-modes)
