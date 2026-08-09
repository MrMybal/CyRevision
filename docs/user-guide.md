# Guide utilisateur

## Premier démarrage

1. Lancez `CyRevision.Desktop`.
2. Utilisez **Ouvrir un dépôt** pour un Git existant, **Créer un dépôt** pour initialiser Git + LFS, ou **Ajouter un dossier** pour un projet sans Git.
3. Dans l’onglet **Projet**, choisissez le mode. Changer de mode ne supprime aucune donnée existante.

## Git

L’onglet **Modifications** indexe ou retire les fichiers de l’index et crée une révision. **Historique** liste les commits. **Branches** crée, ouvre ou fusionne une branche. Les branches reçues des pairs commencent par `cyrevision/`.

Le remote `origin` est facultatif : il peut pointer vers GitHub, GitLab, Forgejo, SSH, un chemin réseau ou un autre dépôt local. Les boutons Fetch/Pull/Push restent inactifs en pratique tant qu’aucun remote n’est configuré.

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

## Sauvegardes

Dans **Sauvegardes**, choisissez un dossier local, NAS ou volume monté, puis la stratégie :

- `CurrentStateOnly` : dernier état uniquement ;
- `DeletedFiles` : historique conservé selon âge/budget ;
- `LimitedVersions` : nombre maximal de snapshots ;
- `Timeline` : limite combinée versions/âge/budget ;
- `Permanent` : conservation sans purge automatique.

Un snapshot est dédupliqué. La colonne **Ajouté** correspond donc aux nouveaux octets réellement écrits, pas à la taille logique totale.

Restaurez d’abord vers un dossier vide et vérifiez le contenu avant de remplacer un projet actif.

## Diff assets

Choisissez deux fichiers ou sélectionnez un changement Git puis **Sélection Git ↔ HEAD**. Les textures produisent une heatmap ; les OBJ une vue filaire cyan/magenta ; les `.uasset` et `.umap` donnent un rapport binaire et les symboles/types détectables hors moteur.
