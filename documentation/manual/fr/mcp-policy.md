---
id: mcp-policy
title: Serveurs MCP et blocage d'urgence
description: Configurer les serveurs MCP locaux ou distants par projet et restreindre chaque serveur ou outil.
section: Code et IA
order: 25
sourceRevision: 1
---

## Transports {#transports}
L'onglet MCP prend en charge les processus STDIO et les adresses HTTP streamable. Configurez commandes, arguments, dossier de travail, environnement, URL, OAuth, variable de jeton bearer et en-têtes HTTP. STDIO est disponible avec les fournisseurs Codex locaux. Les fournisseurs Responses API utilisent uniquement les MCP HTTP distants, exigent le droit réseau et une liste d'outils explicitement autorisés, puis chargent l'autorisation depuis la variable d'environnement configurée.

## Politique des outils {#tool-policy}
Chaque serveur possède un interrupteur, une capacité lecture seule ou lecture/écriture, une exigence réseau, des délais, une liste autorisée, une liste interdite, un mode d'approbation par défaut et des exceptions par outil. La liste interdite est prioritaire. Une demande d'approbation API n'est jamais acceptée silencieusement; seule une politique Approve explicite autorise l'exécution immédiate.

## Isolation du projet {#project-isolation}
Les profils restent dans la configuration locale CyRevision, hors de Git. Le blocage des serveurs Codex non gérés n'expose que les serveurs du projet. Les serveurs ReadWrite exigent le droit de modification; HTTP et réseau exigent le droit réseau.

## Blocage d'urgence {#emergency-block}
BLOCK ALL MCP conserve le blocage de tous les serveurs et annule l'exécution IA CyRevision en cours. Unblock retire l'urgence sans retirer les autres restrictions. MCP est désactivé par défaut. Préférez les variables d'environnement pour les secrets.

```image
{"id": "ai", "src": "assets/ai.fr.svg", "alt": "Vue d'ensemble du fonctionnement", "controls": "minimal"}
```

[Voir aussi](doc:code-and-ai)
