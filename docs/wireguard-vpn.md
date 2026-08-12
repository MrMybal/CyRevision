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

## Initialisation guidée de l'ordinateur

Dépliez **Guided setup · computer, firewall, router** et choisissez le rôle réel de la machine :

- **client uniquement** : aucune règle entrante et aucune modification du routeur ;
- **hôte entrant** : ouvre le port UDP WireGuard configuré ;
- **Unreal Swarm** : ajoute TCP 8008-8009, limité au sous-réseau VPN ;
- **API de contrôle CyRevision** : ajoute TCP 47831, limité au sous-réseau VPN.

Cliquez sur **Diagnose this computer**. CyRevision détecte l'adresse IPv4 LAN, la passerelle, l'interface active et l'outil de pare-feu. Les commandes sont toujours affichées avant application. **Apply firewall rules** demande une autorisation administrateur et ne crée que les règles nommées `CyRevision-VPN-<projet>-*`. **Remove CyRevision rules** retire uniquement ces règles.

Après activation du tunnel, **Test tunnel and peers** envoie un ping privé à chaque adresse VPN puis lit `wg latest-handshakes`. Un handshake récent confirme que le trafic WireGuard revient bien jusqu'à cette machine ; aucun service Internet de test n'est utilisé.

| Plateforme | Comportement |
| --- | --- |
| Windows | règles Windows Defender Firewall créées avec `New-NetFirewallRule` après UAC ; |
| Ubuntu/Debian | règles UFW directes via `pkexec`, si UFW est installé ; |
| Fedora/RHEL et dérivés | ports/règles firewalld permanents puis rechargement ; |
| macOS | guide vers **Réglages Système > Réseau > Pare-feu > Options** pour autoriser l'application ou le service WireGuard signé. |

Références officielles : [New-NetFirewallRule](https://learn.microsoft.com/powershell/module/netsecurity/new-netfirewallrule), [pare-feu macOS](https://support.apple.com/guide/mac-help/change-firewall-settings-on-mac-mh11783/mac), [UFW Ubuntu](https://documentation.ubuntu.com/server/how-to/security/firewalls/) et [firewall-cmd](https://firewalld.org/documentation/man-pages/firewall-cmd).

## Modem, routeur et NAT

Seule la machine **hôte entrant** nécessite une redirection :

1. Réservez son adresse IPv4 LAN dans la section DHCP du routeur.
2. Cherchez **NAT**, **Port forwarding**, **Virtual server** ou **Redirection de ports**.
3. Créez une règle UDP : port externe WireGuard vers la même adresse LAN et le même port.
4. Ne publiez jamais les ports Swarm 8008/8009 ni l'API 47831 : ils passent à l'intérieur du VPN.
5. Utilisez l'IP publique ou un nom DNS dynamique dans l'endpoint du projet.
6. Testez depuis un autre réseau, par exemple la connexion mobile d'un téléphone.

Si l'adresse WAN affichée par le routeur est privée ou appartient à `100.64.0.0/10`, le fournisseur peut utiliser du CGNAT. Une redirection locale ne suffira alors pas : demandez une IPv4 publique ou utilisez comme nœud central un serveur disposant d'une adresse publique. CyRevision n'active jamais UPnP automatiquement.

## Échange simplifié via Sync

Après avoir créé une invitation ou une réponse signée, **Share current message** la place dans `vpn-bootstrap/messages` du dossier d'échange Syncthing dédié. Les pairs autorisés utilisent **Refresh Sync inbox**, sélectionnent le message puis **Load selected**.

- seuls les formats invitation/réponse VPN signés sont acceptés ;
- l'enveloppe contient un SHA-256 et doit correspondre aux métadonnées signées ;
- tout champ ressemblant à une clé privée, un token, un mot de passe, un webhook ou un secret est refusé ;
- charger un message remplit seulement la zone d'échange : **Rejoindre** ou **Accepter** reste obligatoire ;
- si Sync est arrêté, le message est mis en attente localement et partira au prochain démarrage.

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

Dans Swarm Agent, utilisez l'adresse VPN ou l'alias local du coordinateur comme `CoordinatorRemotingHost`. L'assistant CyRevision peut appliquer une règle Windows TCP 8008/8009 limitée au sous-réseau WireGuard après confirmation administrateur.

Références officielles : [WireGuard Quick Start](https://www.wireguard.com/quickstart/), [gestion des tunnels WireGuard Windows](https://git.zx2c4.com/wireguard-windows/about/docs/enterprise.md) et [Unreal Swarm](https://dev.epicgames.com/documentation/unreal-engine/unreal-swarm-in-unreal-engine?lang=en-US).

### Assistant Swarm CyRevision

Dans **Swarm over VPN**, choisissez `Agent` ou `CoordinatorAndAgent`, puis l'adresse VPN et l'alias local du coordinateur. CyRevision peut :

- détecter `SwarmAgent.exe`, `SwarmCoordinator.exe` et `SwarmAgent.Options.xml`, avec sélection manuelle possible ;
- sauvegarder le XML existant, puis modifier uniquement les champs Swarm déjà présents (`CoordinatorRemotingHost`, groupes, filtre d'agents et cache) ;
- créer un bloc DNS local marqué par projet dans le fichier `hosts` Windows et retirer uniquement ce bloc ;
- créer la règle Windows TCP 8008-8009 limitée au sous-réseau VPN ;
- tester l'adresse WireGuard locale, le DNS, les exécutables, le XML, le pare-feu et les deux ports du coordinateur ;
- proposer pour chaque échec une correction concrète lorsque l'action ne peut pas être automatisée.

Fermez Swarm Agent avant d'appliquer son XML. Les ports 8008/8009 ne doivent jamais être redirigés sur le modem. Le plugin Unreal autonome fournit aussi **Tools > Swarm over VPN** pour configurer/lancer/tester Swarm sans CyRevision ; l'application ajoute les opérations administrateur et le diagnostic complet.

## Transfert et dossier partagé dans le VPN

L'onglet **VPN files** expose un service CyRevision minimal, et non un partage SMB :

- il écoute uniquement l'adresse IPv4 WireGuard du projet et refuse une source hors de son sous-réseau VPN ;
- chaque commande exige le jeton aléatoire 256 bits du projet ; transmettez ce jeton aux pairs autorisés par un canal de confiance distinct ;
- **Send file to inbox** envoie un fichier vers un dossier privé sans jamais écraser un nom existant ;
- le dossier partagé est choisi explicitement, peut être listé et téléchargé selon trois permissions séparées ;
- la taille annoncée et le SHA-256 sont vérifiés, un transfert interrompu reste temporaire puis est supprimé ;
- les chemins absolus, `..`, jonctions et liens symboliques ne peuvent pas sortir de la racine partagée.

WireGuard chiffre le transport ; le jeton ajoute l'autorisation applicative. Le port TCP par défaut `47842` est ouvert uniquement pour le sous-réseau VPN par l'assistant. Aucun accès Git, Sync ou dossier de projet n'est ajouté implicitement.

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
