# CyRevision

CyRevision est un client de révision, synchronisation et sauvegarde pensé pour les projets lourds, notamment Unreal Engine. Il fonctionne sans GitHub : Git et Git LFS restent locaux, tandis que la synchronisation P2P optionnelle transporte des transactions sûres entre appareils autorisés.

## Ce qui fonctionne

- application desktop native Windows/Linux/macOS avec Avalonia ;
- catalogue de projets et cinq modes : **Git**, **Git + Sync**, **Sync**, **Sync + versions**, **Backup** ;
- Git local : statut, index, commits, explorateur interactif, comparaison A ↔ B, historique par fichier, branches, merge, remotes et Git LFS ;
- Time Machine LFS : chronologie des objets, emplacements local/pair/archive, demande signée à la demande, reprise des transferts, aperçu des textures, export et restauration confirmée ;
- visualisations Git optionnelles : commits, co-modifications, activité d'équipe et dépendances Unreal simplifiées hors moteur ;
- Git P2P intelligent : bundles et inventaires signés, priorités LFS paramétrables, transferts reprenables et objets vérifiés par SHA-256, sans synchroniser le `.git` actif ;
- Syncthing optionnel avec profil, identité, base, API loopback et port distincts pour chaque projet ;
- invitations à usage unique, code transmis séparément, certificats ECDSA, rôles et révocation ;
- snapshots dédupliqués par SHA-256, restauration, rétention et copie non destructive vers une archive froide ;
- plan de synchronisation intelligent et paramétrable, sans démarrage implicite de Syncthing ;
- diff hors moteur : texte, texture + heatmap, OBJ + superposition 3D, binaire et inspection simplifiée `.uasset`/`.umap` ;
- serveur Linux optionnel avec API, planification des backups, échange Git et tableau de bord web protégé ;
- plugin Unreal Editor optionnel avec ouverture du client et réservations souples non bloquantes des assets.
- VPN WireGuard optionnel intégré : setup, clés, tunnel isolé, pairs VPN-only et profils Unreal Swarm/CI/services ;
- interface multilingue : anglais par défaut, français inclus et catalogues JSON extensibles sur le client et le dashboard web ;

## Ouvrir dans Rider

Ouvrez simplement `CyRevision.sln` dans JetBrains Rider. Le projet cible actuellement **.NET 8**, disponible sur la machine de développement. Une migration vers .NET 10 LTS pourra être faite lorsque tous les environnements ciblés l’auront installé.

Prérequis :

- .NET SDK 8 ou plus récent ;
- Git ;
- Git LFS ;
- Syncthing uniquement si le mode Sync est utilisé ;
- WireGuard uniquement si le mode VPN est utilisé ;
- plugin Avalonia pour la prévisualisation XAML dans Rider, recommandé mais non obligatoire.

## Compiler et lancer

```powershell
dotnet restore CyRevision.sln
dotnet build CyRevision.sln
dotnet test CyRevision.sln
dotnet run --project src/CyRevision.Desktop/CyRevision.Desktop.csproj
```

Publication Windows :

```powershell
./scripts/publish.ps1
```

Publication Linux :

```bash
./scripts/publish.sh
```

## Règle de sécurité Syncthing

CyRevision ne recherche, ne configure et n’arrête jamais une installation Syncthing déjà active. Un processus n’est lancé qu’après activation de Sync et sélection explicite de l’exécutable. Seule la référence du processus créé par CyRevision peut être arrêtée.

Pour Git + Sync, le dossier partagé contient des bundles Git signés, des certificats et des objets LFS immuables. Le working tree et `.git` restent locaux. En mode Sync sans Git, Syncthing partage directement le dossier choisi.

## Documentation

- [Guide utilisateur](docs/user-guide.md)
- [Architecture](docs/architecture.md)
- [Sécurité](docs/security.md)
- [Serveur Linux](docs/linux-server.md)
- [Diff hors moteur](docs/asset-diff.md)
- [API serveur](docs/server-api.md)
- [VPN WireGuard](docs/wireguard-vpn.md)
- [Visualisations Git](docs/git-visualizations.md)
- [Explorateur Git et Time Machine LFS](docs/git-explorer-lfs-time-machine.md)
- [Synchronisation intelligente et archive froide](docs/smart-sync-and-cold-archive.md)
- [Localisation](docs/localization.md)
- [Pont Unreal](plugins/CyRevisionUnreal/README.md)
