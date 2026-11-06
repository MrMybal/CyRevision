---
id: server-api
title: API HTTP du serveur
description: Utiliser les routes authentifiées de projets, Git, sauvegardes, pairs, VPN et discussion.
section: Parcours / Collaboration
parent: server-setup
order: 145
sourceRevision: 1
---

## Authentification et réponses {#authentication}

GET /health et GET /api/v1/capabilities ne demandent pas de jeton API. Les autres routes /api/v1 exigent l’en-tête Authorization: Bearer <token>. Ce jeton administre le serveur ; il est distinct d’une appartenance signée au projet et d’une identité WireGuard.

Les requêtes/réponses utilisent JSON et les noms d’énumérations sous forme de chaînes. Les identifiants de projet et de pair des routes sont des GUID. Un jeton invalide retourne 401. Les erreurs reconnues d’élément absent, d’accès refusé et d’opération invalide correspondent à 404, 403 et 400 ; les autres échecs retournent des détails de problème 500. Lisez aussi le résultat de l’opération.

## Routes {#routes}

Ajoutez /api/v1 devant chaque chemin ci-dessous. Les noms d’entrée désignent des propriétés JSON sauf indication de paramètres de requête.

| Méthode | Chemin | Entrée et résultat |
| --- | --- | --- |
| GET | `/projects` | Définitions des projets enregistrés. |
| POST | `/projects` | name, preset/existingPath facultatifs ; 201 et le projet créé. |
| DELETE | `/projects/{projectId}` | Retirer l’enregistrement serveur ; 204. |
| GET | `/projects/{projectId}/git/status` | État Git, ou 204 si indisponible. |
| GET | `/projects/{projectId}/git/history` | Révisions récentes. |
| POST | `/projects/{projectId}/git/exchange` | Résultat de l’échange signé. |
| GET | `/projects/{projectId}/backups` | Entrées de sauvegarde. |
| POST | `/projects/{projectId}/backups` | Créer une sauvegarde et retourner son résultat. |
| POST | `/projects/{projectId}/sync/configure` | executablePath ; configuration du runtime dédié. |
| POST | `/projects/{projectId}/sync/start` | Démarrer le service ; état courant. |
| POST | `/projects/{projectId}/sync/pause` | Mettre le service en pause ; état courant. |
| POST | `/projects/{projectId}/sync/stop` | Arrêter le service ; état courant. |
| GET | `/projects/{projectId}/sync/status` | État du runtime de synchronisation. |
| POST | `/projects/{projectId}/peers/invitations` | role facultatif ; exchangeText, verificationCode et expiration. |
| POST | `/projects/{projectId}/peers/join-request` | exchangeText et verificationCode ; texte de réponse signé. |
| POST | `/projects/{projectId}/peers/approve` | exchangeText ; texte d’appartenance approuvé. |
| POST | `/projects/{projectId}/peers/membership` | exchangeText ; importer l’autorisation signée. |
| GET | `/projects/{projectId}/peers` | Membres autorisés. |
| DELETE | `/projects/{projectId}/peers/{deviceId}` | Révoquer l’appareil choisi ; 204. |
| POST | `/projects/{projectId}/vpn/configure` | networkCidr, localAddress, listenPort, publicEndpoint, capabilities facultatifs ; profil. |
| GET | `/projects/{projectId}/vpn/profile` | Profil VPN. |
| GET | `/projects/{projectId}/vpn/status` | État du runtime du tunnel. |
| POST | `/projects/{projectId}/vpn/start` | Démarrage explicite du tunnel. |
| POST | `/projects/{projectId}/vpn/stop` | Arrêt explicite du tunnel. |
| POST | `/projects/{projectId}/vpn/invitations` | capabilities facultatif ; exchangeText signé et expiration. |
| POST | `/projects/{projectId}/vpn/join` | exchangeText, capabilities facultatif ; réponse signée. |
| POST | `/projects/{projectId}/vpn/accept` | exchangeText ; accepter la réponse signée. |
| DELETE | `/projects/{projectId}/vpn/peers/{peerId}` | Retirer le pair VPN ; 204. |
| GET | `/projects/{projectId}/chat/snapshot` | Paramètres de requête user/since facultatifs ; état de la discussion. |
| POST | `/projects/{projectId}/chat/messages` | Demande de message ; résultat d’envoi. |
| POST | `/projects/{projectId}/chat/channels` | Demande de canal ; résultat de création. |
| GET | `/projects/{projectId}/chat/messages/{messageId}/attachment` | Pièce jointe sérialisée en JSON. |

## Utilisation {#example}

Utilisez les modes retournés par capabilities au lieu de supposer que tous les modes du bureau sont disponibles. Une réponse HTTP réussie n’autorise pas implicitement un pair : suivez invitation, vérification et appartenance. Le VPN ne remplace pas les permissions Git/Sync.

Remplacez le marqueur de jeton seulement dans votre client local. Ne l’enregistrez pas dans un exemple partagé, une URL ou un profil public.

[Configurer le serveur](doc:server-setup)
