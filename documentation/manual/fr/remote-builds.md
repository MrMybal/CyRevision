---
id: remote-builds
title: Compilation distante via VPN
description: Compiler sur une machine CI et récupérer les artefacts sans publier une branche temporaire.
section: Build et VPN
order: 34
sourceRevision: 1
---

## Installation de l'agent {#agent-setup}
Installez BuildAgent sur la machine CI puis lancez CyRevision.Build.Agent --print-token. Le premier lancement crée un token et un agent.json vide. Ajoutez l'identifiant exact du projet et les recettes. L'opérateur du serveur choisit l'exécutable, les arguments, le timeout, le workspace et les artefacts ; le client ne peut transmettre aucune commande.

## Réseau {#network}
Le test local utilise 127.0.0.1:47841. Pour un pair distant, liez l'agent à son IPv4 WireGuard, ajoutez --allow-private-http et activez Remote build agent over VPN dans l'assistant pare-feu. Ne redirigez jamais TCP 47841 sur le modem.

## Modes source {#source-modes}
ExistingWorkspace ne transfère aucun code et utilise le workspace configuré. UploadedSnapshot envoie les fichiers Git suivis et non ignorés, sans .git ni caches générés, puis les extrait dans un job isolé avec contrôle des chemins et tailles.

## Exécution {#run-and-download}
Dans Remote builds, saisissez l'endpoint VPN, le token, la recette, le mode et le dossier d'artefacts. Test agent affiche les recettes autorisées. Start remote build suit les logs, permet l'annulation et télécharge le ZIP final. Aucun commit ou branche temporaire n'est envoyé au remote.

```image
{"id": "ci", "src": "assets/ci.fr.svg", "alt": "Vue d'ensemble du fonctionnement", "controls": "minimal"}
```

[Voir aussi](doc:unreal-builds)
