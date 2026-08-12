# Guide utilisateur

## Premier démarrage

1. Lancez `CyRevision.Desktop`.
2. Utilisez **Ouvrir un dépôt** pour un Git existant, **Créer un dépôt** pour initialiser Git + LFS, ou **Ajouter un dossier** pour un projet sans Git.
3. Dans l’onglet **Projet**, choisissez le mode. Changer de mode ne supprime aucune donnée existante.

## Git

L’onglet **Modifications** indexe ou retire les fichiers de l’index et crée une révision. **Historique** liste les commits. **Branches** crée, ouvre ou fusionne une branche. Les branches reçues des pairs commencent par `cyrevision/`.

Le remote `origin` est facultatif : il peut pointer vers GitHub, GitLab, Forgejo, SSH, un chemin réseau ou un autre dépôt local. Les boutons Fetch/Pull/Push restent inactifs en pratique tant qu’aucun remote n’est configuré.

L’onglet **Git graphs** ajoute deux visualisations locales optionnelles : un réseau nodal des commits et une carte des relations entre fichiers modifiés ensemble. Vous pouvez limiter le nombre de commits et de fichiers, ainsi que choisir d’inclure toutes les branches. L’analyse est strictement en lecture seule et ne lance aucun fetch. Voir [git-visualizations.md](git-visualizations.md).

Dans **Git LFS**, ajoutez par exemple :

```text
*.uasset
*.umap
*.fbx
*.wav
*.exr
```

## Sync

1. Choisissez un profil contenant Sync.
2. Ouvrez l’onglet **Synchronisation**.
3. Sélectionnez l’exécutable Syncthing. CyRevision ne réutilise pas celui qui tourne déjà.
4. Cliquez **Démarrer**.

En Git + Sync, **Échanger Git maintenant** publie un bundle signé et importe les bundles autorisés. Un commit déclenche aussi cet échange lorsque Sync est actif.

### Ajouter un pair

Sur le propriétaire :

1. choisissez un rôle et créez l’invitation ;
2. envoyez le grand bloc JSON par le canal principal ;
3. envoyez le code à six chiffres par un autre canal.

Sur le nouvel appareil :

1. collez l’invitation, saisissez le code et cliquez **Préparer la demande** ;
2. renvoyez la demande au propriétaire.

Le propriétaire colle la demande et clique **Approuver**. Le nouvel appareil importe ensuite le certificat retourné. L’invitation ne peut plus être réutilisée.

Le propriétaire peut sélectionner un membre actif et cliquer **Révoquer**. Les fichiers déjà copiés sur cet appareil ne peuvent évidemment pas être effacés à distance.

## VPN WireGuard

Le VPN ne dépend pas du mode choisi dans **Projet**. Dans **VPN WireGuard**, choisissez **Installation système** pour utiliser WireGuard déjà installé, ou **Runtime intégré** pour utiliser les composants WireGuard officiels livrés avec CyRevision. Configurez ensuite le moteur : CyRevision génère les clés du projet et prépare une interface `cyrev-*` séparée de vos autres tunnels.

Dépliez **Guided setup**. Pour un poste qui rejoint seulement un réseau, laissez le mode client : aucun port entrant ni réglage de modem n'est nécessaire. Pour le serveur, coordinateur ou poste qui reçoit les connexions, activez le rôle hôte, lancez le diagnostic, appliquez les règles CyRevision après confirmation puis ouvrez la page du routeur. L'assistant indique l'adresse LAN à réserver et l'unique redirection UDP à créer.

Si Sync est configuré, utilisez **Share current message** après avoir créé une invitation ou une réponse. Les autres pairs autorisés la verront dans la boîte Sync VPN. La clé privée ne quitte jamais la machine et charger un message ne l'accepte pas automatiquement.

Indiquez un endpoint public `hôte:port` sur le nœud qui reçoit les connexions, choisissez sa fonction puis créez une invitation VPN. Le nouvel appareil configure WireGuard localement, colle l'invitation et renvoie la réponse signée ; le propriétaire l'accepte explicitement. Un pair VPN-only ne reçoit aucun accès Git ou Sync.

Pour Unreal Swarm, utilisez de préférence un coordinateur Windows permanent comme nœud invitant. CyRevision affiche son IP VPN ; renseignez-la dans `CoordinatorRemotingHost` et autorisez TCP 8008/8009. Le guide complet se trouve dans [wireguard-vpn.md](wireguard-vpn.md).

L'onglet **Swarm over VPN** automatise maintenant ce workflow : rôle Agent/Coordinator, chemins Swarm, groupes, cache, sauvegarde et mise à jour du XML, alias DNS local réversible, lancement des processus et test complet avec correction par échec. Le plugin Unreal propose le même point d'entrée en mode autonome sous **Tools > Swarm over VPN**.

L'onglet **VPN files** crée une inbox privée et un dossier partagé explicite entre pairs WireGuard. Enregistrez le profil, appliquez la règle pare-feu VPN, démarrez l'endpoint puis choisissez un pair pour tester, envoyer, parcourir ou télécharger. Copiez le jeton de projet uniquement aux appareils autorisés via un canal distinct ; sa rotation arrête l'endpoint et invalide immédiatement les anciennes copies.

## Sauvegardes

Dans **Sauvegardes**, choisissez un dossier local, NAS ou volume monté, puis la stratégie :

- `CurrentStateOnly` : dernier état uniquement ;
- `DeletedFiles` : historique conservé selon âge/budget ;
- `LimitedVersions` : nombre maximal de snapshots ;
- `Timeline` : limite combinée versions/âge/budget ;
- `Permanent` : conservation sans purge automatique.

Un snapshot est dédupliqué. La colonne **Ajouté** correspond donc aux nouveaux octets réellement écrits, pas à la taille logique totale.

Restaurez d’abord vers un dossier vide et vérifiez le contenu avant de remplacer un projet actif.

## Explorateur de code et recherche globale

L'onglet **Code** fournit un explorateur complet du projet, indépendant de Rider et d'Unreal Editor. Les dossiers lourds ou générés (`.git`, `bin`, `obj`, `Binaries`, `Intermediate`, `Saved`, `DerivedDataCache`, `node_modules`) sont exclus automatiquement. Utilisez le filtre de chemin pour réduire l'arborescence, ou activez explicitement les fichiers cachés.

Le raccourci **Ctrl+Shift+F** ouvre la recherche globale et place le curseur dans le champ. La recherche accepte la casse exacte, les mots entiers, les expressions régulières et des motifs de fichiers tels que `*.cs;*.cpp;*.h`. CyRevision utilise `ripgrep` lorsqu'il est disponible et bascule sur son moteur .NET dans le cas contraire.

Sélectionnez un fichier pour afficher son aperçu, ses symboles et son historique Git. Pour suivre seulement une partie du code, sélectionnez une ou plusieurs lignes dans l'aperçu puis cliquez **History of selection** : CyRevision utilise l'historique Git de la plage au lieu d'afficher tous les commits du fichier. La sélection d'un dossier affiche l'historique de tout ce sous-arbre.

## Assistant IA facultatif

Activez **AI Workspace** depuis l'onglet **Plugins**, puis ouvrez **AI Assistant**. Les fournisseurs proposés sont Codex CLI, l'API OpenAI Responses, une API compatible, ainsi que Codex avec Ollama ou LM Studio. Une clé API saisie dans l'interface reste uniquement en mémoire pendant la session et est effacée après l'exécution.

Les droits sont accordés projet par projet : lecture obligatoire, modification de fichiers, réseau, indexation Git et commit. Codex démarre en sandbox `read-only`; `workspace-write` n'est utilisé que si la modification a été cochée. CyRevision ne donne jamais de droit de push automatique à l'agent. Les opérations `git add` et `git commit` sont effectuées par CyRevision après une exécution réussie, uniquement si les options correspondantes ont été activées.

## Diff assets

Choisissez deux fichiers ou sélectionnez un changement Git puis **Sélection Git ↔ HEAD**. Les textures produisent une heatmap ; les OBJ une vue filaire cyan/magenta ; les `.uasset` et `.umap` donnent un rapport binaire et les symboles/types détectables hors moteur.

## Réservations souples Unreal

Après installation du plugin `CyRevisionUnreal`, sélectionnez un ou plusieurs assets dans le Content Browser puis ouvrez le menu contextuel **CyRevision** :

- **Signaler : je travaille dessus** indique votre présence ;
- **Libérer mon signalement** retire uniquement vos marqueurs ;
- **Voir les réservations souples** ouvre la fenêtre récapitulative dans Unreal.

Le signalement n'effectue ni checkout, ni changement de permission, ni verrou Git LFS. Si quelqu'un travaille déjà sur l'asset, Unreal affiche son nom mais permet de continuer. Le marqueur est renouvelé chaque minute et expire après 30 minutes par défaut si l'éditeur est fermé ou ne peut plus actualiser la présence.

Le client externe montre les mêmes informations dans **Travail en cours**. En Git + Sync, elles circulent dans la zone d'échange, hors de Git. En Sync seul, elles circulent sous `.cyrevision/presence`. Quand Sync est arrêté, la fonction reste utilisable localement et les marqueurs partiront au prochain démarrage de Sync.
