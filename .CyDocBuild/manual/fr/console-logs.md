---
id: console-logs
title: Console, journaux et état des opérations
description: Distinguer diagnostics de l'app, commandes Git et sortie de compilation.
section: Parcours / Code et ressources
parent: code-explorer
order: 23
sourceRevision: 2
---

## Où regarder {#where-to-look}
Code & Assets > Console & Logs réunit sorties de commandes du dépôt et événements de l'application. Les logs de compilation CI sont dans CI / Actions ou PR > CI ; ceux des builds Unreal locaux dans Unreal builds. Choisir la bonne source évite de confondre un avertissement de l'app avec une erreur du compilateur.

## Examiner {#investigate}
1. Notez projet sélectionné et heure du problème.
2. Filtrez texte/gravité si proposé.
3. Examinez l'opération associée et son état final.
4. Copiez uniquement l'extrait utile au support.

La barre d'état affiche l'opération courante. Task details ouvre les opérations actives/récentes ; les rafraîchissements ne doivent pas forcer son ouverture. Une analyse annulée par de nouveaux événements disque n'est pas un échec d'écriture.

## Console du dépôt {#repository-console}
Les commandes s'exécutent dans le dépôt sélectionné et peuvent modifier fichiers/historique. Lisez paramètres et commande ; ne collez pas une commande destructive inexpliquée issue d'un log ou message. Annuler n'inverse pas une commande déjà terminée.

Les journaux peuvent contenir chemins, arguments et sortie distante. Masquez les identifiants d'accès et travaux non concernés avant partage. N'effacez l'affichage qu'après avoir conservé les éléments de reproduction nécessaires.

```image
{"id":"ci","src":"assets/ci.fr.svg","alt":"Console, journaux et état des opérations","controls":"minimal"}
```

[Voir aussi](doc:diagnostics)
