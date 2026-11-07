# CyRevision 0.1.34 Alpha

[English](release-0.1.34.md) · [Release et téléchargements](https://github.com/MrMybal/CyRevision/releases/tag/v0.1.34)

## Changements

- Mise à jour du lecteur documentaire intégré vers l’architecture CyDoc actuelle.
- Mise à disposition de 86 pages en anglais et en français : parcours actuels, routes du serveur et signatures, paramètres, retours et exemples de l’API publique des plugins.
- Ajout d’une navigation arborescente, d’une barre latérale élargie, d’une recherche en haut avec Ctrl+K, d’infobulles de titres complets, de la restauration des positions de lecture et de six thèmes.
- Ajout d’une galerie de l’interface, de captures et de schémas traduits, avec agrandissement dans le lecteur et commandes clavier.
- Ouverture du chapitre adapté depuis F1 et l’aide contextuelle, dans Native V2 comme dans Classic, même sans projet sélectionné.
- Conservation des anciens liens documentaires et distribution du lecteur local complet dans Documentation/Documentation.html.
- Séparation des outils de rédaction du manuel distribué et génération du lecteur pendant les builds de l’application.
- Génération de révisions documentaires identiques depuis des sources Windows ou Unix.

Windows V2 nécessite Windows 10/11 x64 et Direct3D 11. Lancez
`CyRevision.Native.exe` depuis le ZIP portable. Le raccourci **CyRevision (Classic)**
et `CyRevision.Desktop.exe` restent disponibles ; Linux et macOS utilisent
l’interface Avalonia.

L’interface V2 reste en Alpha. Les fonctions Git sont en Bêta ; Sync, Git + Sync,
Lore, Backup et les fonctions des plugins restent en Alpha.

## Problèmes signalés

Les notes publiées pour cette version ne fournissent pas de liste détaillée des bugs. Consultez le [véritable suivi des problèmes](https://github.com/MrMybal/CyRevision/issues) pour les signalements et leur évolution ; cela ne signifie pas que cette version ne comporte aucun bug.

## Limites documentées

- Les fonctions Alpha peuvent être incomplètes ou évoluer entre les versions. Conservez des sauvegardes indépendantes et validez la restauration avant la production.
- Native V2 est réservé à Windows x64/Direct3D 11. Les paquets Linux et macOS utilisent Classic Avalonia, pas le moteur de rendu natif V2.
- Les paquets Windows ne sont pas signés. Les applications macOS ont une signature d’intégrité ad hoc et ne sont pas notariées ; le système peut afficher des avertissements de confiance.
- L’inspection hors ligne des packages Unreal est une lecture simplifiée de métadonnées, pas un rendu de l’Unreal Editor. Les parcours propres à un moteur nécessitent son installation hôte.
- Git, Git LFS, les outils VPN optionnels et les intégrations aux fournisseurs/éditeurs ont leurs propres prérequis d’installation ou de configuration.
- Le manuel CyDoc complet est inclus localement ; aucun site documentaire public n’est actuellement lié pour cette version. Conservez Documentation.html et DocData ensemble dans le dossier Documentation.

[Installation et prérequis](../README.fr.md#installation) · [Tous les fichiers et sommes de contrôle](https://github.com/MrMybal/CyRevision/releases/tag/v0.1.34)
