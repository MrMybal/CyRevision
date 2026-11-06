---
id: vpn
title: WireGuard, services et Unreal Swarm
description: Créer un VPN facultatif indépendant des permissions Git et Sync.
section: Parcours / Collaboration
parent: sync-and-peers
order: 31
sourceRevision: 2
---

## Module indépendant {#independent-module}
Le VPN peut fonctionner seul ou avec Git et Sync. Rejoindre un VPN n'accorde jamais automatiquement un accès au dépôt ou au dossier synchronisé.

## Choix du moteur {#backend-choice}
Installation système utilise WireGuard déjà installé sur la machine. Runtime intégré utilise les composants officiels livrés sous VpnRuntime et valide chaque fichier requis avec les SHA-256 de runtime.json. Le choix appartient au projet et ne peut changer que lorsque son tunnel CyRevision est arrêté.

## Initialisation guidée {#guided-computer-setup}
Un client simple n'a besoin d'aucune règle entrante ni modification du routeur. Un hôte entrant ouvre uniquement son port UDP WireGuard. Les règles optionnelles Swarm TCP 8008-8009 et contrôle CyRevision TCP 47831 sont limitées au sous-réseau VPN. Le diagnostic détecte l'adresse LAN, la passerelle et le pare-feu. Les plans Windows Defender, UFW et firewalld sont appliqués seulement après confirmation administrateur ; macOS suit l'écran officiel du pare-feu applicatif. Les règles CyRevision portent des noms déterministes par projet et peuvent être retirées sans toucher aux autres règles.

## Routeur / modem {#router-modem}
Réservez l'adresse LAN détectée puis redirigez un seul port UDP WireGuard vers le même port local. Ne redirigez jamais Swarm ou l'API de contrôle. Testez depuis un autre réseau. Une adresse WAN privée ou dans 100.64.0.0/10 peut indiquer du CGNAT et nécessiter un serveur public ou une IP publique du fournisseur. CyRevision n'active jamais UPnP automatiquement.

## Admission via sync {#sync-assisted-onboarding}
L'invitation ou la réponse signée actuelle peut être placée dans l'échange Sync dédié. Seuls les messages VPN publics signés sont acceptés ; le SHA-256 et les métadonnées signées sont vérifiés, les champs ressemblant à des secrets sont refusés et les clés privées restent locales. Charger un message ne le rejoint ou ne l'accepte jamais automatiquement.

## Unreal swarm {#unreal-swarm}
Utilisez des adresses VPN stables pour le coordinator et les agents. Sous Windows, Swarm utilise TCP 8008 et 8009 ; l'assistant garde ces ports à l'intérieur du VPN.

```image
{"id":"vpn","src":"assets/vpn.fr.svg","alt":"WireGuard, services et Unreal Swarm","controls":"minimal"}
```

[Voir aussi](doc:members-security)
