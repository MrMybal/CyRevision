---
id: server-setup
title: Pair serveur facultatif
description: Connecter un pair disponible en continu sans dépendance obligatoire pour Git local.
section: Parcours / Collaboration
parent: sync-and-peers
order: 145
sourceRevision: 1
---

## Quand l’utiliser {#when-to-use-it}

CyRevision Server est un pair facultatif disponible en continu avec sauvegardes et tableau de bord web. Git sur le bureau reste utilisable sans lui. Démarrer un serveur ne l’autorise pas sur un projet.

## Préparer un déploiement {#deployment}

Utilisez deploy/docker-compose.yml fourni et son fichier d’environnement d’exemple. Choisissez un volume de données dédié et réglez CYREVISION_SERVER_TOKEN dans l’environnement local du déploiement. Le tableau de bord utilise le port 8080 ; placez un reverse proxy HTTPS devant lui sur un réseau non fiable.

Le déploiement ordinaire n’accorde aucun privilège d’administration réseau. Ajoutez la surcharge compose VPN seulement si ce serveur doit utiliser WireGuard. Git, Sync et Backup n’en ont pas besoin. L’unité systemd fournie écoute en boucle locale par défaut et utilise un fichier d’environnement géré par l’administrateur.

## Connecter et vérifier {#connect-and-verify}

1. Vérifiez /health et /api/v1/capabilities.
2. Ouvrez le tableau de bord et saisissez le jeton API pour cette session du navigateur.
3. Enregistrez le projet voulu et son emplacement côté serveur.
4. Configurez et démarrez uniquement les services Sync ou VPN nécessaires.
5. Examinez les états finaux du projet et des services avant les échanges.

Sans jeton configuré, le serveur crée config/server-token.txt sous son dossier de données. Lisez-le localement avec le compte de service ; ne l’exposez jamais par un partage public.

## Rétention et restauration {#retention}

Choisissez explicitement le stockage des sauvegardes. Les fichiers courants synchronisés ne sont pas une sauvegarde. Séparez rétention et copies de récupération, et vérifiez une restauration dans un autre dossier avant de supprimer les originaux.

[API HTTP](doc:server-api) · [Appartenance](doc:members-security)
