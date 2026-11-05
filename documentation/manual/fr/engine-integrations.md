---
id: engine-integrations
title: Intégrations Unity, Godot et Lore
description: Utiliser le bon adaptateur sans supposer une compatibilité universelle.
section: Extensions
order: 38
sourceRevision: 1
---

## Activer le fournisseur adapté {#enable-the-matching-provider}
Dans Plugins, activez Unity, Godot ou Lore selon le besoin. Sélectionnez la racine contenant les métadonnées du moteur. Son espace apparaît si le plugin est actif et la détection réussie.

## Configurer et vérifier {#configure-and-verify}
1. Lisez projet/version détectés.
2. Choisissez l'exécutable ou fichier demandé par l'adaptateur.
3. Appliquez et contrôlez l'état avant lancement/installation.
4. Ouvrez le moteur propriétaire pour valider le résultat.

Chaque adaptateur expose ses fonctions réelles ; les moteurs n'ont pas forcément des possibilités identiques. N'utilisez pas l'installateur Unreal pour Unity/Godot. Activer côté application et installer côté projet sont deux étapes distinctes.

## Mode Lore {#lore-mode}
Lore est en Alpha et peut fournir son mode, ses branches/historique ou actions de push. Apply/Switch/Push modifient réellement le projet : lisez les confirmations. Ne supposez pas que l'historique Lore est Git si le projet ne l'utilise pas explicitement aussi.

## Détection incorrecte {#troubleshoot-detection}
Contrôlez racine, fichier de métadonnées, plugin et onglets visibles. Un projet moteur imbriqué peut nécessiter son propre dossier plutôt que le parent. Actualisez après modification externe des métadonnées. Si une version n'est pas compatible, conservez l'intégration existante plutôt que forcer un paquet inadéquat.

```image
{"id": "integrations", "src": "assets/integrations.fr.svg", "alt": "Intégrations Unity, Godot et Lore", "controls": "minimal"}
```

[Voir aussi](doc:unreal-plugin)
