---
id: unreal-builds
title: Compilations Unreal locales et diagnostics
description: Choisir cible/outils, lancer un build borné et examiner la sortie.
section: Extensions
parent: unreal-plugin
order: 39
sourceRevision: 2
---

## Préparer {#prepare}
Activez l'intégration Unreal et choisissez un projet détecté. Ouvrez Unreal builds. Cette vue compile localement ; elle est distincte de GitHub Actions et des agents distants.

## Configurer {#configure}
1. Choisissez version moteur, cible, plateforme et configuration.
2. Réglez SDK/NDK, Java ou outils Linux requis.
3. Choisissez destination, délai maximal et parallélisme.
4. Activez Cook and package uniquement si souhaité.
5. Examinez un preset avant application ou sauvegarde.

L'auto-configuration aide à détecter les outils ; elle n'installe pas tous les SDK propriétaires et n'accepte pas leurs licences à votre place. Vérifiez les composants de votre installation moteur.

## Exécuter et examiner {#execute-and-inspect}
Lancez le build proposé ou choisissez une plage de versions pour validation en lot. Results, Errors and warnings et Full log sont distincts. Lisez le contexte autour des diagnostics ; le filtre peut omettre la cause. Une compilation réussie sur une version ne valide pas toutes les autres.

Les builds consomment CPU, RAM et disque. Limitez la concurrence et ne supprimez pas sorties/outils pendant l'exécution. Si l'échec précède la compilation, vérifiez association du projet, composants installés et commande avant de changer le code.

```image
{"id":"ci","src":"assets/ci.fr.svg","alt":"Compilations Unreal locales et diagnostics","controls":"minimal"}
```

[Voir aussi](doc:remote-builds)
