---
id: tray-and-startup
title: Zone de notification et lancement automatique
description: Conserver les services facultatifs en arrière-plan après avoir masqué la fenêtre.
section: Bases
order: 44
sourceRevision: 1
---

## Zone de notification {#system-tray}
CyRevision affiche une icône native sous Windows, macOS et les bureaux Linux compatibles. Un clic affiche ou masque la fenêtre. Le menu indique le projet sélectionné et propose l'actualisation, les préférences de démarrage et une commande Quitter explicite.

## Fermer et quitter {#close-and-quit}
Close window to tray garde CyRevision actif avec le bouton de fermeture de la fenêtre. Utilisez File > Exit ou Quit CyRevision depuis l'icône pour arrêter réellement l'application et ses services. Désactiver l'icône désactive également la fermeture vers le tray et le démarrage masqué.

## Lancement automatique {#automatic-startup}
Launch at system login enregistre CyRevision uniquement pour l'utilisateur courant : clé Run Windows, entrée XDG autostart Linux ou LaunchAgent macOS. Start hidden in tray ajoute --background afin de ne pas ouvrir la fenêtre à la connexion. L'enregistrement vise toujours l'exécutable actuellement lancé. Une version portable doit donc rester au même emplacement.

## Note linux {#linux-note}
L'icône exige la prise en charge StatusNotifier/AppIndicator par le bureau. Si elle n'apparaît pas, désactivez le démarrage masqué afin que la fenêtre reste accessible.

```image
{"id": "settings", "src": "assets/settings.fr.svg", "alt": "Vue d'ensemble du fonctionnement", "controls": "minimal"}
```

[Voir aussi](doc:application-updates)
