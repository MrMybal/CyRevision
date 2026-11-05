---
id: pull-request-manager
title: Gestionnaire de pull requests
description: Examiner, créer, récupérer, commenter, fermer et fusionner les pull requests GitHub dans CyRevision.
section: Git
order: 13
sourceRevision: 1
---

## Connexion {#connection}
Sélectionnez un projet Git puis ouvrez Pull Requests. CyRevision détecte le remote origin GitHub. La lecture d'un dépôt public peut fonctionner sans jeton ; les dépôts privés et les écritures utilisent un jeton de session masqué ou la variable d'environnement configurée. Les jetons ne sont jamais enregistrés.

## Espace de revue {#review-workspace}
Filtrez les demandes ouvertes, fermées ou toutes. Examinez les fichiers modifiés, patches unifiés, commentaires, reviews, branches, brouillons, statistiques de modification et possibilité de fusion. Créez une demande, publiez un commentaire général, approuvez, demandez des changements ou ouvrez la page du fournisseur. Les reviews générales et la conversation sont disponibles ; les actions sur une ligne dépendent des capacités exposées par le fournisseur.

## Checkout sûr {#safe-checkout}
Le checkout s'arrête si le working tree contient des changements. CyRevision récupère la tête dans une référence dédiée puis crée ou avance uniquement cyrevision/pr-N ; la branche active n'est jamais réinitialisée de force.

## Actions distantes {#remote-actions}
Fermer, rouvrir et fusionner modifient le dépôt distant et exigent une confirmation. Choisissez merge, squash ou rebase selon la politique du dépôt. Ces actions peuvent déclencher la CI ou un déploiement.

## Fournisseurs {#providers}
GitHub est le premier adaptateur, avec surcharge d'API Enterprise. Le cœur reste indépendant du fournisseur pour de futurs adaptateurs GitLab, Forgejo et de revue décentralisée. Git, LFS, Sync et les sauvegardes restent utilisables sans forge.

## Onglets, états et tri {#tabs-state-and-sorting}
Description, Files, Conversation, CI, Conflicts et Actions séparent les usages. L'en-tête # trie les numéros numériquement. Open/closed/merged est distinct de l'état CI. Les marques d'auteur/PR personnelle et couleurs complètent les libellés.

## CI et conflits {#ci-and-conflicts}
CI réunit runs, jobs, logs complets, filtres erreurs/avertissements et recherche. Agrandissez le log pour les lignes longues. Un ancien succès ne doit pas masquer un contrôle courant en attente/en cours. Clic droit sur un job actif : annulation si admissible ; elle peut viser le run parent entier et le serveur contrôle les droits. Conflicts liste les chemins seulement lorsque le fournisseur/analyse locale les fournit.

## Après fusion {#after-merge}
Actions peut proposer la suppression locale confirmée de la branche liée. Vérifiez qu'elle n'est pas active et ne contient rien d'utile non publié. Les tâches Jira/ClickUp détectées peuvent changer d'état selon politique ou validation manuelle. Fusion de PR et mise à jour de tâche sont deux résultats à contrôler séparément.

```image
{"id": "ci", "src": "assets/ci.fr.svg", "alt": "Vue d'ensemble du fonctionnement", "controls": "minimal"}
```

[Voir aussi](doc:ci-actions)
