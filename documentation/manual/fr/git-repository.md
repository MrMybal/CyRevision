---
id: git-repository
title: Configuration du dépôt et remotes
description: Configurer la connexion à l'hébergeur depuis l'interface.
section: Git
order: 6
sourceRevision: 1
---

## Emplacement et prérequis {#location-and-prerequisites}
Sélectionnez un projet Git puis Overview > Git repository. Contrôlez racine, branche, état et remotes. Enregistrer un dossier n'initialise pas Git.

## Ajouter un remote {#add-a-remote}
1. Saisissez son nom, généralement origin.
2. Collez l'URL de clonage HTTPS ou SSH fournie par l'hébergeur.
3. Utilisez Add remote, actualisez puis vérifiez les URL fetch/push.
4. Faites Fetch avant de choisir un upstream ou publier.

L'URL désigne le dépôt ; les identifiants Git ou SSH authentifient séparément. N'insérez ni jeton ni mot de passe dans l'URL. Les formats reconnus permettent d'ouvrir la page web du dépôt. Un hébergeur personnalisé peut nécessiter une ouverture manuelle.

## Modifier et retirer {#modify-and-remove}
Sélectionnez le bon remote avant de changer son URL ou le retirer. Retirer sa configuration ne supprime pas le dépôt hébergé mais peut affecter le suivi upstream. Vérifiez ensuite les branches.

## Fetch, Pull et Push {#fetch-pull-and-push}
Fetch actualise les références sans fusionner. Pull intègre l'upstream dans la branche courante. Push publie les commits et peut être refusé par l'authentification, les protections ou une divergence. Examinez l'erreur au lieu de forcer. Un dépôt Git local reste utilisable sans remote.

```image
{"id": "git", "src": "assets/git.fr.svg", "alt": "Configuration du dépôt et remotes", "controls": "minimal"}
```

[Voir aussi](doc:git-workflow)
