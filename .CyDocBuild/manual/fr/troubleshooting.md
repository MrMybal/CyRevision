---
id: troubleshooting
title: Dépannage et sécurité
description: Diagnostiquer les outils absents, échanges bloqués, transferts partiels et problèmes Git courants.
section: Aide et récupération
order: 47
sourceRevision: 2
---

## Outils non détectés {#tools-not-detected}
Vérifiez que git --version et git lfs version fonctionnent dans un terminal. Redémarrez CyRevision après une modification du PATH. Syncthing et WireGuard restent facultatifs jusqu'à l'activation de leurs modules.

## Push refusé {#push-rejected}
Effectuez d'abord Fetch et examinez la branche entrante. N'utilisez jamais un push forcé sans vouloir réellement remplacer l'historique distant.

## Échange entre pairs bloqué {#pair-exchange-not-moving}
Vérifiez que l'instance Sync dédiée fonctionne, que le pair est connecté et que son certificat possède toujours un rôle autorisé à écrire Git. Un fichier présent dans le dossier d'échange n'est jamais importé sans inventaire signé d'un pair autorisé.

## Fichiers lfs partiels {#partial-lfs-files}
Les fichiers .cyrevision-partial sont des zones de reprise locales. Ne les renommez pas manuellement dans le stockage LFS. Le prochain échange vérifie le préfixe existant et termine ou redémarre proprement la copie.

## Récupération sûre {#safe-recovery}
Conservez le working tree, le .git actif, le stockage des backups et l'archive froide dans des emplacements séparés. Avant toute suppression, créez un snapshot et vérifiez sa restauration dans un autre dossier.

```image
{"id":"settings","src":"assets/settings.fr.svg","alt":"Dépannage et sécurité","controls":"minimal"}
```

[Voir aussi](doc:diagnostics)
