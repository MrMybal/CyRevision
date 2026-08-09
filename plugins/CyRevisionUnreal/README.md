# CyRevision Collaboration pour Unreal Engine

Ce plugin Editor optionnel ajoute **Outils > Ouvrir dans CyRevision**. Il lance le client externe avec le dossier du projet courant ; CyRevision peut donc gérer Git, LFS, Sync, backups et diffs sans garder Unreal Editor ouvert.

Il ajoute aussi des **réservations souples** dans le menu contextuel des assets du Content Browser :

- **Signaler : je travaille dessus** publie une présence informative ;
- **Libérer mon signalement** retire uniquement la présence de l'utilisateur courant ;
- **Voir les réservations souples** affiche les assets, personnes, machines et dates ;
- deux personnes peuvent signaler le même asset : un avertissement est affiché, mais rien n'est bloqué ;
- aucun checkout Unreal, changement de permission ou verrou Git LFS n'est effectué ;
- le signalement est renouvelé chaque minute et expire automatiquement après 30 minutes par défaut.

En mode Git + Sync, les marqueurs sont placés dans la zone d'échange CyRevision synchronisée, hors du dépôt Git. En mode Sync sans Git, ils résident sous `.cyrevision/presence`. Si le projet n'est pas encore connu de CyRevision, le plugin fonctionne localement dans `Saved/CyRevision/Presence` et l'indique dans sa fenêtre.

## Installation

1. Copiez `CyRevisionUnreal` dans le dossier `Plugins` du projet Unreal.
2. Ajoutez la section de `Config/DefaultEditorPerProjectUserSettings.ini.example` à la configuration utilisateur du projet et adaptez le chemin.
3. Facultatif : définissez `AdvisoryDisplayName` et `AdvisoryExpirationMinutes`.
4. Régénérez les fichiers de projet puis compilez l'Editor.

Le pont cible l'API Editor commune à Unreal Engine 5.3–5.6. La réservation souple est volontairement indépendante d'un fournisseur Source Control : elle fonctionne avec Git, Git LFS, Sync seul, ou sans synchronisation active.
