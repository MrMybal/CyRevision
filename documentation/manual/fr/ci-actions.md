---
id: ci-actions
title: Exécutions CI et logs complets
description: Rechercher dans les logs, filtrer erreurs/avertissements et gérer les runs.
section: Git
order: 14
sourceRevision: 1
---

## Ouvrir un run {#open-a-run}
Ouvrez Git > CI / Actions, actualisez workflows/exécutions et sélectionnez-en une. Les jobs se chargent séparément. Comparez branche, révision et dates : un ancien succès peut coexister avec la dernière compilation en attente/en cours.

## Lire le log complet {#read-the-complete-log}
1. Choisissez run et job puis Full log / Logs.
2. Attendez le téléchargement/extraction.
3. Choisissez All, Errors ou Warnings. Recherche et gravité se combinent.
4. Défilez horizontalement ou ouvrez la fenêtre agrandie pour les lignes longues.

Source et numéros de ligne restent visibles. Le classement est heuristique : un filtre Errors vide ne prouve pas la réussite. Revenez à All pour le contexte et lisez la conclusion. Les logs actifs, expirés ou restreints peuvent être indisponibles ; Error summary n'est pas le log complet.

## Lancer, relancer, annuler {#dispatch-rerun-and-cancel}
Choisissez workflow et référence Git avant le lancement. Le workflow doit accepter le déclenchement manuel. Rerun failed concerne les runs terminés admissibles. Cancel exige les droits Actions/écriture. Le clic droit sur un job peut annuler tout le run parent, pas uniquement ce job : lisez la confirmation. Le serveur vérifie les droits même si l'action apparaît localement.

PR > CI utilise les mêmes lecteur, recherche et filtres. Actualisez après de nouveaux commits. En attente/en cours n'est pas une réussite.

```image
{"id": "ci", "src": "assets/ci.fr.svg", "alt": "Exécutions CI et logs complets", "controls": "minimal"}
```

[Voir aussi](doc:pull-request-manager)
