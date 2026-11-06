---
id: plugins
title: Plugins et fonctions optionnelles
description: Activer explicitement les fournisseurs sans lier tous les outils.
section: Extensions
order: 36
sourceRevision: 2
---

## Trouver et activer {#locate-and-enable}
Ouvrez Extensions & Help > Plugins. Examinez fournisseurs installés, état et description. Activez uniquement la fonction utile. La plupart des plugins sont en Alpha ; disponibilité ne signifie pas maturité de production.

## Configurer dans le bon contexte {#configure-in-context}
1. Activez le fournisseur et appliquez ses réglages.
2. Sélectionnez le projet voulu et contrôlez sa détection.
3. Ouvrez l'espace du plugin ou son mode de fonctionnement.
4. Réglez exécutable/compte externe, testez la connexion et lisez l'état.

Un plugin désactivé ne doit pas démarrer un service à l'ouverture d'un projet. Une intégration moteur peut être installée alors que le moteur ne tourne pas. La désactivation ne désinstalle pas nécessairement le plugin côté projet et ne retire pas ses données.

## Un onglet manque ? {#if-a-tab-is-absent}
Vérifiez Overview > Visible tabs, le mode et le type de projet (.uproject, métadonnées Unity/Godot ou format du fournisseur). Un mode de plugin peut regrouper les onglets sous sa catégorie. Revenez à un projet compatible avant de conclure à un échec.

N'installez que des paquets de confiance. Vérifiez la compatibilité avant une mise à jour en équipe et fermez l'éditeur si requis. Pour développer une extension, lisez le chapitre API plutôt que modifier les préférences de l'app.

```image
{"id":"integrations","src":"assets/integrations.fr.svg","alt":"Plugins et fonctions optionnelles","controls":"minimal"}
```

[Voir aussi](doc:plugin-api)
