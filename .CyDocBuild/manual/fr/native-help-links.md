---
id: native-help-links
title: Adresses de l’aide contextuelle
description: Ouvrir une page, une section ou une illustration locale validée depuis un hôte natif.
section: Développeurs / API publique
parent: plugin-api
kind: api
order: 150
sourceRevision: 1
---

## Contrat {#contract}

L’hôte natif C++ inclut `DocumentationLinks.h`. `bool isLocalDocumentationLink(const std::string& url)` accepte la forme d’URL du lecteur installé et refuse les caractères de contrôle ; c’est un contrôle ciblé du lecteur, pas un service général d’autorisation du disque.

L’hôte géré reçoit `docs.open` avec `topicId`, puis `anchor` et `mediaId` facultatifs, et retourne une `url`. Le champ IPC `id` est réservé à la corrélation des requêtes. L’hôte valide page et section contre le manifeste installé avant d’ouvrir le navigateur.

Le lecteur installé est `Documentation/Documentation.html` ; langue/page/section restent dans le fragment d’URL. Une API de conversion chemin/URI encode espaces et caractères non ASCII avant l’ajout du fragment.

F1 vise la vue courante sans changer de projet ni abandonner les brouillons. L’identifiant de page reste stable en anglais et en français.

[Aide clavier](doc:keyboard-shortcuts)
