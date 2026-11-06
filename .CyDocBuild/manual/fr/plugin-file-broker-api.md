---
id: plugin-file-broker-api
title: Accès aux fichiers du projet
description: Énumérer, lire et écrire uniquement dans les capacités accordées par l’hôte.
section: Développeurs / API publique
parent: plugin-api
kind: api
order: 150
sourceRevision: 1
---

## Opérations {#operations}

Importez `CyRevision.Plugin.Abstractions`. L’hôte transmet `IPluginProjectFileSandbox` lors de l’activation du projet. Sa `Policy` décrit l’identifiant et la racine du projet, le plugin, les permissions, les racines relatives et les tailles maximales de lecture/écriture. Les budgets par défaut de `PluginProjectSandboxPolicy` sont de 64 Mio par opération.

`EnumerateFilesAsync` exige le droit d’énumération. `ReadAllBytesAsync` exige la lecture. `WriteAllBytesAsync` exige l’écriture. Fournissez des chemins relatifs au projet. Les chemins absolus, traversées, racines non autorisées et liens sortant du projet sont refusés. Les écritures utilisent un remplacement atomique.

```csharp
using CyRevision.Plugin.Abstractions;
byte[] data = await files.ReadAllBytesAsync("Config/DefaultEngine.ini", cancellationToken);
```

Ce fragment suppose que l’hôte a déjà accordé cette racine relative et un budget de lecture suffisant pour le fichier.

## Limite {#boundary}

Le courtier protège les extensions coopératives ; il n’isole pas une DLL .NET chargée dans le processus au niveau du système. Un plugin dans le processus reste du code de confiance. Demander des capacités réseau/processus ne crée pas un processus protégé distinct.

[Activation](doc:plugin-contracts-api#project-context)
