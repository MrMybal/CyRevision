---
id: discord-agent
title: Agent de projet Discord
description: Publier les mises à jour Git groupées dans un salon Discord sans dépendre de GitHub.
section: Intégrations
order: 35
sourceRevision: 1
---

## Webhook du salon {#channel-webhook}
Créez un webhook entrant dans le salon Discord qui doit recevoir les notifications, puis collez son URL dans l'onglet Agent Discord. Le webhook appartient à un salon précis : CyRevision n'a pas besoin d'un token de bot global.

## Modes d'exécution {#execution-modes}
Intégré fonctionne dans l'application desktop. Autonome fonctionne dans un sidecar multi-projet et continue après la fermeture du desktop. Lancer l'agent local démarre le sidecar livré ; un serveur ou pair VPN distant peut exécuter le même programme. L'API de contrôle utilise un jeton Bearer, écoute sur loopback par défaut, impose HTTPS à distance et n'autorise HTTP privé qu'après validation explicite d'un VPN WireGuard de confiance.

## Point de départ sûr {#safe-baseline}
Le premier démarrage mémorise le HEAD actuel sans publier l'ancien historique. Les vérifications suivantes regroupent les nouveaux commits dans un message et peuvent signaler les changements de branche active. Les Pull, échanges entre pairs et commits locaux sont détectés dès qu'ils deviennent visibles dans le dépôt Git local.

## Anti-doublon {#duplicate-prevention}
CyRevision enregistre un point de contrôle par projet uniquement après l'acceptation du message par Discord. Exécutez la surveillance automatique sur un seul agent par projet : deux agents actifs possèdent des points de contrôle locaux indépendants et peuvent annoncer la même mise à jour.

## Secrets et mentions {#secrets-and-mentions}
Le webhook et le jeton de contrôle autonome restent dans la configuration locale de l'utilisateur, hors du projet et du dépôt. L'API ne renvoie jamais le webhook enregistré. Les messages automatiques désactivent les mentions Discord.

## Limites {#limits}
L'agent annonce l'historique Git visible localement et fonctionne sans GitHub. Les pull requests, issues et reviews appartiennent à une forge et nécessitent l'intégration Discord de cette forge.

```image
{"id": "integrations", "src": "assets/integrations.fr.svg", "alt": "Vue d'ensemble du fonctionnement", "controls": "minimal"}
```

[Voir aussi](doc:members-security)
