# Agent Discord

Le module `CyRevision.Discord` publie les mises à jour Git visibles localement dans un salon Discord précis. Il fonctionne avec ou sans GitHub : la destination est définie par un webhook entrant créé dans le salon.

## Configuration

1. Dans Discord, ouvrez les paramètres du salon, puis **Intégrations > Webhooks**.
2. Créez un webhook et copiez son URL.
3. Dans CyRevision, ouvrez **Agent Discord**, collez l’URL et enregistrez.
4. Utilisez **Envoyer un test** pour confirmer la destination.
5. Démarrez l’agent, ou activez son démarrage automatique pour ce projet.

Le premier démarrage mémorise le commit courant sans publier l’ancien historique. Les vérifications suivantes regroupent les nouveaux commits et peuvent aussi signaler un changement de branche active. Les commits deviennent détectables après une création locale, un Pull ou un échange P2P importé.

## Sécurité et anti-doublon

- Le webhook reste dans le dossier de configuration de l’utilisateur, jamais dans le projet ni dans Git.
- Son URL est masquée après l’enregistrement et n’est jamais recopiée dans les journaux ou messages.
- Les mentions Discord sont désactivées dans tous les messages automatiques.
- Le point de contrôle n’avance qu’après une réponse Discord réussie ; un échec sera donc retenté.
- Une seule machine doit démarrer automatiquement l’agent pour un projet. Deux machines ont des états locaux indépendants et peuvent publier la même mise à jour.
- **Retirer le webhook** supprime le profil local et son point de contrôle.

## Limites

CyRevision observe le dépôt Git local. Il ne connaît pas les pull requests, issues ou reviews propres à GitHub/GitLab. Pour ces événements, utilisez en complément l’intégration Discord de la forge concernée.
