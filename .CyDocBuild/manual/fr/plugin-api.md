---
id: plugin-api
title: API des plugins et applications
description: Créer des extensions limitées au projet avec des permissions de fichiers explicites et une isolation clairement définie.
section: Extensions
parent: plugins
order: 43
sourceRevision: 2
---

## Activation par projet {#project-activation}
Un plugin est activé indépendamment pour chaque projet. Ses onglets, modes, fournisseurs de preview et services disparaissent dès sa désactivation pour le projet sélectionné.

## Contrats {#contracts}
Référencez CyRevision.Plugin.Abstractions et implémentez ICyRevisionPlugin. Des interfaces facultatives ajoutent les previews, modes de projet, outils IA, moteurs de jeu, Lore, Perforce ou le cycle de vie du projet sans référencer Avalonia.

## Courtier de fichiers {#file-broker}
IPluginProjectFileSandbox accorde explicitement l’énumération, la lecture et l’écriture dans des racines relatives choisies. Les chemins absolus, traversées, racines non autorisées, contenus trop volumineux et liens symboliques sortant du projet sont refusés. Les écritures sont atomiques.

## Frontière de sécurité {#security-boundary}
Une DLL .NET chargée dans le processus reste du code de confiance et ne constitue pas un bac à sable du système. Le courtier protège les extensions coopératives. Une extension exigeant une isolation forte du réseau, des processus ou du disque doit fonctionner dans une application ou un worker externe.

## Réglages par défaut {#defaults}
Commencez en lecture seule, demandez séparément les droits d’écriture, réseau et processus, excluez les secrets des manifests et affichez une preview avant toute action destructive ou distante.

```image
{"id":"integrations","src":"assets/integrations.fr.svg","alt":"API des plugins et applications","controls":"minimal"}
```

## Référence publique {#public-reference}

- [Cycle de vie des plugins et activation du projet](doc:plugin-contracts-api)
- [Accès aux fichiers du projet](doc:plugin-file-broker-api)
- [Contrats de suivi des tâches](doc:work-item-api)
- [Modes de projet fournis par un plugin](doc:project-mode-api)
- [API de connexion des compagnons éditeur](doc:engine-plugin-api)
- [Fournisseurs d’aperçu et de comparaison](doc:file-presentation-api)
- [Contrats de stockage CyStore](doc:cystore-api)
- [Adresses de l’aide contextuelle](doc:native-help-links)

[Voir aussi](doc:plugins)
