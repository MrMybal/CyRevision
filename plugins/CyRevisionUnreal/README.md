# CyRevision Bridge pour Unreal Engine

Ce plugin Editor optionnel ajoute **Outils > Ouvrir dans CyRevision**. Il lance le client externe avec le dossier du projet courant ; CyRevision peut donc gérer Git, LFS, Sync, backups et diffs sans garder Unreal Editor ouvert.

## Installation

1. Copiez `CyRevisionUnreal` dans le dossier `Plugins` du projet Unreal.
2. Ajoutez la section de `Config/DefaultEditorPerProjectUserSettings.ini.example` à la configuration utilisateur du projet et adaptez le chemin.
3. Régénérez les fichiers de projet puis compilez l'Editor.

Le pont cible l'API Editor commune à Unreal Engine 5.3–5.6. Le futur fournisseur Source Control natif sera un module séparé afin de ne pas rendre ce pont obligatoire.
