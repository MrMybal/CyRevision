---
id: diagnostics
title: Diagnostics de performance et dépannage
description: Recueillir des éléments reproductibles et limiter le travail superflu.
section: Aide et récupération
parent: troubleshooting
order: 46
sourceRevision: 2
---

## Examiner plutôt que deviner {#inspect-rather-than-guess}
Extensions & Help > Diagnostics présente mesures et échantillons récents. Actualisez explicitement ; Clear samples efface les mesures, pas le dépôt. Console & Logs et Task details donnent le contexte des opérations.

## Reproduire {#reproduce}
1. Notez version, système, thème/densité et échelle d'affichage.
2. Notez mode du projet, vue, filtre et action exacte.
3. Attendez l'indicateur ou annulez une opération annulable réellement bloquée.
4. Recommencez sur un petit projet de démonstration et comparez.

## Améliorer la réactivité {#improve-responsiveness}
Bornez analyses graphe/historique, ciblez les recherches et évitez les scans répétés des sorties générées. Préférez des exclusions adaptées à la désactivation de toute surveillance. Gardez de l'espace sur les disques de cache/build. Réduisez les builds Unreal simultanés si CPU/RAM/disque saturent.

## Signaler {#report-a-problem}
Donnez résultat attendu/observé, étapes minimales et extraits de log utiles masqués. La capture doit montrer vue et chargement. Ne supprimez ni dépôt, sauvegardes ni profil privé pour corriger l'interface. Si elle paraît bloquée, cherchez une confirmation dans une fenêtre auxiliaire et essayez View > Reset panel layout.

```image
{"id":"settings","src":"assets/settings.fr.svg","alt":"Diagnostics de performance et dépannage","controls":"minimal"}
```

[Voir aussi](doc:troubleshooting)
