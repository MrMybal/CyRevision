---
id: swarm-and-vpn-files
title: Sessions Swarm et fichiers VPN sécurisés
description: Configurer Unreal Swarm sur WireGuard et échanger des fichiers sans exposer un partage public.
section: Parcours / Collaboration
parent: vpn
order: 32
sourceRevision: 2
---

## Session swarm {#swarm-session}
Ouvrez Swarm over VPN après avoir configuré le tunnel du projet. Choisissez Agent ou CoordinatorAndAgent, sélectionnez les exécutables Unreal correspondants et le fichier SwarmAgent.Options.xml existant, puis renseignez IPv4 VPN, alias local, groupes, filtre et cache facultatif. Fermez Swarm Agent avant l'application. CyRevision crée une sauvegarde horodatée et modifie uniquement les champs connus déjà présents dans le XML.

## Dns et pare-feu {#dns-and-firewall}
Apply local DNS alias remplace uniquement le bloc marqué de ce projet dans le fichier hosts Windows. L'assistant pare-feu crée les règles TCP 8008-8009 déterministes limitées au sous-réseau WireGuard. Ne redirigez jamais ces ports sur le modem.

## Diagnostic complet {#full-diagnosis}
Run full diagnosis vérifie Windows, l'adresse VPN assignée, les exécutables, le XML Agent, la résolution DNS, le pare-feu du projet et les deux ports du Coordinator. Chaque avertissement ou échec contient une correction concrète. CyRevisionUnreal propose aussi Tools > Swarm over VPN pour configurer, lancer et tester en mode autonome.

## Endpoint de fichiers vpn {#vpn-file-endpoint}
VPN files écoute uniquement l'IPv4 WireGuard du projet et refuse toute source hors de son CIDR. Chaque commande exige le jeton 256 bits du projet. Choisissez une inbox privée et une racine partagée explicite, puis accordez séparément réception, parcours et téléchargement. Transmettez le jeton aux pairs autorisés par un autre canal de confiance.

## Sécurité du transfert {#transfer-safety}
Un upload n'écrase jamais un fichier. Uploads et downloads vérifient taille et SHA-256 avant déplacement atomique. Les fichiers partiels sont supprimés après échec. Chemins absolus, remontées parent, jonctions et liens symboliques ne peuvent pas sortir de la racine. La rotation du jeton invalide les anciennes copies et arrête l'endpoint jusqu'à son redémarrage.

```image
{"id":"vpn","src":"assets/vpn.fr.svg","alt":"Sessions Swarm et fichiers VPN sécurisés","controls":"minimal"}
```

[Voir aussi](doc:vpn)
