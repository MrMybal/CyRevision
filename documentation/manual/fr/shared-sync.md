---
id: shared-sync
title: Dossiers partagés hors du projet
description: Partager uniquement des dossiers explicites avec les pairs autorisés.
section: Synchronisation
order: 28
sourceRevision: 1
---

## Créer un partage {#create-a-share}
Team & Network > Shared Sync folders : choisissez un dossier précis existant, un nom clair et sa politique. Ne partagez pas profil utilisateur, disque entier ou projet sans rapport pour quelques fichiers.

## Admettre les participants {#admit-participants}
1. Vérifiez projet actif et identité du pair autorisé.
2. Définissez participants et politique du dossier.
3. Lancez explicitement le service dédié.
4. Contrôlez état des dossiers/appareils et changements en attente des deux côtés.

Un VPN n'accorde pas l'accès aux dossiers synchronisés. Ajouter un appareil et partager un dossier sont deux droits distincts. Vérifiez invitations et rôles par le workflow des membres.

## Résoudre et arrêter {#resolve-and-stop}
Examinez les conflits avant d'effacer une variante. Pause/Stop vise l'instance gérée par le projet, pas un Syncthing personnel. Retirer une configuration de partage n'est pas supprimer le dossier local : lisez les confirmations et préservez son contenu utile.

Des fichiers synchronisés ne sont pas une sauvegarde. Gardez des instantanés indépendants et évitez les caches générés provoquant des analyses permanentes.

```image
{"id": "sync", "src": "assets/sync.fr.svg", "alt": "Dossiers partagés hors du projet", "controls": "minimal"}
```

[Voir aussi](doc:members-security)
