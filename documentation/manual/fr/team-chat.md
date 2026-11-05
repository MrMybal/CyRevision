---
id: team-chat
title: Chat du projet, salons et serveur privé
description: Discutez dans des salons de projet via un VPN, un dossier synchronisé ou votre propre serveur CyRevision.
section: Équipe et réseau
order: 33
sourceRevision: 1
---

## Espace de discussion {#channel-workspace}
L’onglet Chat d’équipe propose des salons par projet, un fil compact, des pièces jointes, l’état de livraison et la présence des membres. Les salons general, development et builds sont disponibles par défaut. Un serveur privé permet de créer d’autres salons persistants.

## Transports {#transports}
VPN établit une connexion directe vers un hôte CyRevision sur une adresse locale, LAN ou WireGuard. Dossier synchronisé écrit un fichier immuable par message dans un répertoire synchronisé et évite les conflits d’un journal partagé. Serveur privé utilise l’API authentifiée de CyRevision.Server et conserve les discussions lorsque les ordinateurs de l’équipe sont hors ligne.

## Serveur privé {#private-server}
Installez CyRevision.Server sur une machine que vous contrôlez, définissez CYREVISION_SERVER_TOKEN et publiez son point d’accès ASP.NET Core derrière HTTPS. Saisissez l’URL et le jeton dans les paramètres du chat. HTTP est refusé sauf activation explicite et uniquement si l’adresse appartient à la boucle locale, au LAN, au CGNAT ou au VPN.

## Fichiers et historique {#files-and-history}
Les pièces jointes sont limitées en taille et vérifiées par SHA-256 au stockage et au téléchargement. Les messages Serveur et Sync sont des fichiers immuables séparés par projet. Les archives locales et synchronisées peuvent être chiffrées. La rétention et la taille maximale restent configurables par projet.

## Sécurité {#security}
Ne publiez jamais le port TCP du chat VPN direct sur un routeur. Pour un serveur accessible depuis Internet, terminez TLS avec un certificat de confiance et protégez le jeton comme un mot de passe. L’accès au chat n’accorde jamais automatiquement les permissions Git, Sync, sauvegarde ou VPN.

```image
{"id": "sync", "src": "assets/sync.fr.svg", "alt": "Vue d'ensemble du fonctionnement", "controls": "minimal"}
```

[Voir aussi](doc:members-security)
