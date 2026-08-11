# VPN WireGuard : système ou intégré

Le VPN est un module indépendant. Un projet peut utiliser uniquement WireGuard, WireGuard + Sync, WireGuard + Git, ou les trois. CyRevision utilise les composants officiels de WireGuard et simplifie leur configuration ; il ne remplace ni le protocole ni sa cryptographie.

## Choix du moteur

- **Installation système** : CyRevision détecte WireGuard déjà installé sur la machine. C'est le mode par défaut et le meilleur choix lorsque WireGuard est déjà administré par l'utilisateur ou l'entreprise.
- **Runtime intégré** : CyRevision utilise une copie dédiée des composants WireGuard placée dans `VpnRuntime/<plateforme>`. Chaque binaire est vérifié avec le SHA-256 déclaré dans `runtime.json` avant son utilisation.

Le choix est enregistré par projet. Il peut être modifié uniquement lorsque le tunnel CyRevision du projet est arrêté. Les tunnels personnels existants ne sont ni importés, ni modifiés, ni arrêtés dans les deux modes.

Le runtime intégré est un paquet natif propre à chaque plateforme : gestionnaire/service WireGuard officiel sous Windows, et implémentation userspace officielle avec `wg`/`wg-quick` sous Linux ou macOS. S'il n'est pas inclus dans une build, l'interface indique précisément le dossier attendu et conserve le mode système utilisable.

## Assistant desktop

1. Ouvrez un projet puis l'onglet **VPN WireGuard**.
2. Choisissez **Installation système** ou **Runtime intégré**, puis cliquez sur **Configurer le moteur sélectionné**. CyRevision valide le moteur, crée une clé privée et une interface `cyrev-xxxxxxxx` propre au projet.
3. Vérifiez le réseau privé, l'adresse et le port. Sur le nœud qui reçoit les connexions, indiquez un endpoint `DNS-ou-IP:port` joignable en UDP.
4. Choisissez la fonction locale : accès général, agent ou coordinateur Swarm, worker CI, ou hôte de service.
5. Enregistrez puis activez. Windows demande l'autorisation administrateur uniquement pour installer le service de ce tunnel.

La clé privée n'apparaît jamais dans l'aperçu. CyRevision n'ajoute aucune route `0.0.0.0/0` : seul le réseau du projet et les routes privées explicitement autorisées passent par le tunnel.

## Ajouter un appareil VPN-only

L'appareil propriétaire crée une invitation signée pour une fonction précise. Le nouvel appareil :

1. configure d'abord WireGuard dans son projet local ;
2. colle l'invitation, choisit la fonction autorisée et clique sur **Rejoindre l'invitation** ;
3. renvoie la réponse signée au propriétaire ;
4. le propriétaire la colle puis clique sur **Accepter la réponse**.

L'identifiant local du projet peut être différent sur les deux machines : l'invitation établit l'association. La réponse ne peut modifier ni l'adresse attribuée ni les capacités signées. L'acceptation reste explicite et une clé, une adresse ou un appareil déjà enregistré est refusé.

Le MVP utilise une topologie en étoile : chaque invité se connecte au nœud qui émet ses invitations. Pour un service commun, faites donc du serveur, du coordinateur Swarm ou de la CI le nœud invitant. La diffusion automatique d'un nouveau pair à tous les membres pour un maillage complet viendra dans une version ultérieure.

## Unreal Swarm

Swarm Coordinator distribue les travaux aux Swarm Agents. Swarm est actuellement Windows-only et utilise TCP 8008 et 8009. Un profil typique est :

- machine permanente Windows : `SwarmCoordinator` et nœud invitant ;
- postes artistes Windows : `GeneralAccess` ou `SwarmAgent` ;
- machines de calcul Windows : `SwarmAgent`, éventuellement VPN-only.

Dans Swarm Agent, utilisez l'adresse VPN du coordinateur comme `CoordinatorRemotingHost`. Autorisez TCP 8008/8009 dans le pare-feu Windows sur l'interface CyRevision. CyRevision affiche l'adresse du coordinateur détecté mais ne modifie pas encore le pare-feu.

Références officielles : [WireGuard Quick Start](https://www.wireguard.com/quickstart/), [gestion des tunnels WireGuard Windows](https://git.zx2c4.com/wireguard-windows/about/docs/enterprise.md) et [Unreal Swarm](https://dev.epicgames.com/documentation/unreal-engine/unreal-swarm-in-unreal-engine?lang=en-US).

## Serveur Linux optionnel

L'API serveur expose la configuration, l'état, le démarrage, l'arrêt et les échanges de pairs VPN. Installez `wireguard-tools`. `wg-quick` a besoin de `CAP_NET_ADMIN` ou de droits root ; CyRevision n'essaie jamais de saisir un mot de passe `sudo`.

Avec systemd, installez le drop-in facultatif `deploy/cyrevision-server-vpn.conf`. Avec Docker Compose :

```bash
docker compose -f docker-compose.yml -f docker-compose.vpn.yml up -d --build
```

Le fichier principal ne donne volontairement pas `NET_ADMIN`. Un serveur Linux peut servir de pair VPN, worker CI ou hôte de service, mais pas d'agent Swarm tant qu'Epic limite Swarm à Windows.

## Coexistence avec WireGuard existant

- CyRevision ne liste pas, ne réécrit et n'arrête pas les autres tunnels.
- Un marqueur de propriété local est requis avant tout arrêt.
- Si une interface ou un service du même nom existe sans ce marqueur, CyRevision signale une collision et ne touche à rien.
- Le mode système Windows utilise uniquement `/installtunnelservice` et `/uninstalltunnelservice` pour le nom du projet ; Linux utilise `wg-quick up/down` sur le fichier du projet.
- Le runtime intégré utilise uniquement ses exécutables vérifiés et ne modifie pas le `PATH` global de la machine.
