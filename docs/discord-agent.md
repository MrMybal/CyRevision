# Agent Discord

Le module `CyRevision.Discord` publie les mises à jour Git visibles localement dans un salon Discord précis. Il fonctionne avec ou sans GitHub : la destination est définie par un webhook entrant créé dans le salon. Il peut s'exécuter dans l'application desktop ou dans un agent autonome placé à côté d'elle.

## Configuration

1. Dans Discord, ouvrez les paramètres du salon, puis **Intégrations > Webhooks**.
2. Créez un webhook et copiez son URL.
3. Dans CyRevision, ouvrez **Agent Discord** et choisissez le mode d'exécution.
4. Collez l’URL et enregistrez.
5. Utilisez **Envoyer un test** pour confirmer la destination.
6. Démarrez l’agent, ou activez son démarrage automatique pour ce projet.

## Modes d'exécution

- **Intégré** : la surveillance vit dans CyRevision Desktop et s'arrête avec sa fenêtre.
- **Autonome local** : **Lancer l'agent local** démarre le sidecar livré dans le dossier `Agent`. Il continue après la fermeture du desktop et peut surveiller plusieurs projets.
- **Autonome distant** : le même sidecar tourne sur un serveur, une CI ou un autre pair. Le plugin de contrôle desktop lui envoie les réglages et les commandes par une API authentifiée.

Le sidecar écoute par défaut sur `http://127.0.0.1:47831`. Son premier lancement crée `control-token.txt` dans son dossier de données. Pour une autre machine, HTTPS est obligatoire par défaut. HTTP sur une adresse privée n'est accepté qu'après avoir coché l'option dédiée pour un VPN WireGuard de confiance.

Exemple autonome manuel :

```text
CyRevision.Discord.Agent --listen http://127.0.0.1:47831 --print-token
```

Sous Linux, le paquet Debian installe également l'unité utilisateur `cyrevision-discord-agent.service`. Activez-la avec `systemctl --user enable --now cyrevision-discord-agent`.

Le premier démarrage mémorise le commit courant sans publier l’ancien historique. Les vérifications suivantes regroupent les nouveaux commits et peuvent aussi signaler un changement de branche active. Les commits deviennent détectables après une création locale, un Pull ou un échange P2P importé.

## Sécurité et anti-doublon

- Le webhook reste dans le dossier de configuration de l’utilisateur, jamais dans le projet ni dans Git.
- Son URL est masquée après l’enregistrement et n’est jamais recopiée dans les journaux ou messages.
- Les mentions Discord sont désactivées dans tous les messages automatiques.
- Le point de contrôle n’avance qu’après une réponse Discord réussie ; un échec sera donc retenté.
- Une seule instance doit démarrer automatiquement l’agent pour un projet. Deux sidecars ou un sidecar et le mode intégré ont des états locaux indépendants et peuvent publier la même mise à jour. Le client essaie donc d'arrêter l'ancien mode lors du basculement.
- Le jeton de contrôle reste dans la configuration utilisateur. Toutes les routes de l'API, y compris l'état de santé, exigent un en-tête Bearer valide.
- L'API ne renvoie jamais le webhook enregistré. Une URL vide conserve le secret existant sur l'agent.
- **Retirer le webhook** supprime le profil local et son point de contrôle.

## Limites

CyRevision observe le dépôt Git local. Il ne connaît pas les pull requests, issues ou reviews propres à GitHub/GitLab. Pour ces événements, utilisez en complément l’intégration Discord de la forge concernée.
