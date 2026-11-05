---
id: members-security
title: Membres, invitations et rôles
description: Séparer autorisations du dépôt, de Sync et du VPN.
section: Équipe et réseau
order: 30
sourceRevision: 1
---

## Emplacement {#location}
Overview > Members : distinguez participants Git/Sync/VPN et membres pairs autorisés. Collaborateur de l'hébergeur, appareil synchronisé et pair VPN ne sont pas le même droit.

## Inviter un appareil {#invite-a-device}
1. Créez l'invitation à usage unique proposée.
2. Transmettez le code par un autre canal fiable.
3. Vérifiez identité de l'appareil et rôle demandé.
4. Approuvez explicitement puis contrôlez l'adhésion.

Un nom ressemblant à un collègue ne suffit pas. Accordez le minimum de capacités nécessaires. Une adresse VPN joignable n'autorise pas à elle seule dépôt ou dossiers partagés.

## Changer ou révoquer {#change-or-revoke}
Vérifiez cible et conséquences avant rôle, rotation d'identifiants ou révocation. Révoquer bloque les opérations futures autorisées mais ne récupère pas les copies déjà livrées. Coordonnez remplacement/récupération avec le propriétaire.

Si un pair manque, contrôlez projet, expiration, approbation et réseau séparément. Ne contournez pas une erreur d'adhésion en partageant des configurations privées de pairs.

```image
{"id": "sync", "src": "assets/sync.fr.svg", "alt": "Membres, invitations et rôles", "controls": "minimal"}
```

[Voir aussi](doc:vpn)
