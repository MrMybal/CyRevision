---
id: plugin-api
title: API des plugins et applications
description: Créer des extensions limitées au projet avec des permissions de fichiers explicites et une isolation clairement définie.
section: Extensions
order: 43
sourceRevision: 1
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
{"id": "integrations", "src": "assets/integrations.fr.svg", "alt": "Vue d'ensemble du fonctionnement", "controls": "minimal"}
```

[Voir aussi](doc:plugins)
