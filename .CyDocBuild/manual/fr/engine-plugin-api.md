---
id: engine-plugin-api
title: API de connexion des compagnons éditeur
description: Détecter les projets moteur et connecter le compagnon Unity ou Godot adapté.
section: Développeurs / API publique
parent: plugin-api
kind: api
order: 150
sourceRevision: 1
---

## Contrat du fournisseur {#provider-contract}

`IGameEngineIntegrationPlugin` étend `ICyRevisionPlugin`. Il expose `Engine`, `BridgeStatus` et l’événement `ProjectChanged`. Un événement de changement indique le moteur, la racine du projet et l’action ; ce n’est pas un commit Git.

Inspectez le projet avant d’installer le compagnon propre à son moteur. Installation et configuration de connexion peuvent modifier la configuration du projet : utilisez uniquement la racine explicitement sélectionnée.

## Portée du pont {#bridge-scope}

Le compagnon utilise un pont local privé pour les projets autorisés. Détection, installation et notifications de changements ne supposent ni rendu natif des ressources ni différences sémantiques. La présentation des fichiers reste un contrat indépendant.

`GameEngineIntegrationPluginBase` est une classe de base facultative côté hôte ; cette référence présente son contrat public plutôt que son implémentation.

[Intégrations des moteurs](doc:engine-integrations)
