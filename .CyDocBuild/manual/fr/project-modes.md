---
id: project-modes
title: Modes de projet et démarrage des services
description: Choisir des fonctions indépendantes pour chaque projet.
section: Bien démarrer
parent: getting-started
order: 2
sourceRevision: 2
---

## Emplacement {#location}
Ouvrez Overview > Project > Operating mode. Le réglage appartient à chaque projet.

## Modes {#modes}
- Git (Bêta) : Git et LFS ; le remote est facultatif.
- Git + Sync (Alpha) : outils de révision et échanges entre pairs autorisés. Synchroniser ne remplace pas les commits.
- Sync (Alpha) : échange des fichiers courants par une instance Syncthing dédiée, sans Git.
- Sync + Versions (Alpha) : synchronisation avec versionnage de fichiers, pas des branches Git.
- Backup (Alpha) : instantanés et rétention sans synchronisation continue.
- Les modes des plugins apparaissent si leur fournisseur est actif. Lore et les plugins restent en Alpha.

## Changer sans perdre son travail {#change-safely}
1. Terminez ou annulez les opérations qui modifient des fichiers.
2. Choisissez le mode et lisez son résumé.
3. Appliquez et confirmez.
4. Vérifiez les onglets disponibles et les services.

Les données existantes sont conservées. Les services sont arrêtés/reconfigurés ; changer de mode n'efface ni l'historique Git ni les sauvegardes. Le démarrage automatique Sync/VPN est un réglage explicite distinct par projet. Choisir un exécutable ne le lance pas.

Si un onglet manque, vérifiez plugins actifs, type de projet détecté et Overview > Visible tabs. Masqué/indisponible n'est pas en chargement.

```image
{"id":"modes","src":"assets/modes.fr.svg","alt":"Modes de projet et démarrage des services","controls":"minimal"}
```

[Voir aussi](doc:getting-started)
