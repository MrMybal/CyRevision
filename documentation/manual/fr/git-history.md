---
id: git-history
title: Historique des commits et fichiers
description: Retrouver les auteurs, examiner LFS et exporter un ancien fichier.
section: Git
order: 8
sourceRevision: 1
---

## Historique des commits {#commit-history}
Ouvrez Git > History et cherchez un message, auteur ou hash. Sélectionnez une révision : le panneau central liste ses fichiers, celui de droite affiche la différence choisie. Attendez la fin des chargements liés quand vous changez de révision.

## Historique d'un fichier, y compris LFS {#file-history-including-lfs}
1. Ouvrez Code & Assets > Solution Explorer.
2. Sélectionnez précisément le fichier ou l'asset.
3. Clic droit > Git / LFS file history.
4. Dans la fenêtre séparée, choisissez un commit : auteur, date, message complet et stockage.

Les versions LFS montrent l'OID du pointeur et la taille attendue. Lire les métadonnées ne télécharge pas tous les anciens assets. Un contenu absent doit être récupéré explicitement avant l'export/aperçu qui nécessite ses octets. Les binaires ont un historique de commits, pas une annotation ligne par ligne pertinente.

## Comparer ou restaurer {#compare-or-restore}
Choisissez une autre révision pour comparer A à B. Export crée un fichier séparé. Restore modifie le fichier de travail : préservez vos changements, vérifiez la destination puis commitez volontairement.

Redimensionnez/déplacez les colonnes et défilez horizontalement. Un historique vide peut désigner un fichier jamais commité, une autre branche ou un ancien chemin renommé. Vérifiez le chemin relatif au dépôt.

```image
{"id": "history-v2", "src": "assets/history-v2.png", "alt": "Historique des commits et fichiers", "controls": "minimal"}
```

[Voir aussi](doc:lfs-time-machine)
