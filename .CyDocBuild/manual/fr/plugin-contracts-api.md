---
id: plugin-contracts-api
title: Cycle de vie des plugins et activation du projet
description: Initialiser un plugin, recevoir un contexte de projet et libérer ses ressources explicitement.
section: Développeurs / API publique
parent: plugin-api
kind: api
order: 150
sourceRevision: 1
---

## Cycle de vie {#lifecycle}

Référencez l’assembly `CyRevision.Plugin.Abstractions` et importez `CyRevision.Plugin.Abstractions`. Implémentez `ICyRevisionPlugin`, exposez son `Descriptor`, implémentez `InitializeAsync` et libérez les ressources possédées avec `IAsyncDisposable.DisposeAsync`.

L’hôte fournit `CyRevisionPluginContext` : `ApplicationDirectory`, `PackageDirectory`, `ConfigurationDirectory`, `DataDirectory` et `ApplicationVersion`. Conservez configuration et données générées dans leurs dossiers prévus ; le dossier de l’application n’autorise pas à parcourir les fichiers de l’utilisateur.

## Contexte de projet {#project-context}

Implémentez `IProjectScopedPlugin` seulement si l’extension a besoin du projet sélectionné. `ActivateProjectAsync` reçoit une `PluginProjectSandboxPolicy` et un courtier de fichiers coopératif. `DeactivateProjectAsync` reçoit l’identifiant du projet. Libérez l’état propre au projet lorsque ce contexte se termine.

Un paquet contient `cyrevision-plugin.json`, son assembly d’entrée et son type d’entrée. Installer un paquet et l’activer pour un projet sont deux actions distinctes. Ne lancez pas tous les services facultatifs à la simple sélection d’un projet.

## Utilisation des contrats {#source}

La référence ci-dessous présente les signatures publiques, le sens des paramètres et les retours. Elle n’expose pas les fichiers d’implémentation. Ces contrats sont indépendants de l’interface : un plugin n’a besoin de référencer ni Avalonia ni Dear ImGui.

[Accès aux fichiers](doc:plugin-file-broker-api) · [Modes de projet](doc:project-mode-api)
