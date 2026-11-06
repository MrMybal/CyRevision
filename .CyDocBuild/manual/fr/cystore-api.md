---
id: cystore-api
title: Contrats de stockage CyStore
description: Examiner, capturer, vérifier et reconstruire les versions du magasin de blocs facultatif.
section: Développeurs / API publique
parent: plugin-api
kind: api
order: 150
sourceRevision: 1
---

## Stockage expérimental {#experimental-storage}

`ICyStorePlugin` respecte le cycle de vie normal des plugins. Il propose un magasin de blocs adressés par contenu indépendant de la base d’objets Git. Les pointeurs LFS ordinaires restent compatibles et ces méthodes ne créent ni ne réécrivent de commits Git.

Inspectez ou initialisez le magasin, capturez des fichiers choisis ou les fichiers LFS suivis, puis énumérez les versions stockées. Les résultats distinguent blocs nouvellement écrits et réutilisés et peuvent signaler fichiers ignorés ou avertissements.

## Vérifier avant reconstruction {#reconstruct}

Choisissez un identifiant retourné par `ListVersionsAsync`. Vérifiez la version, reconstruisez-la et lisez destination et résumé avant d’utiliser les octets. Ne supposez pas que la reconstruction a remplacé un fichier du répertoire de travail. `IProgress<CyStoreProgress>` facultatif indique étape, chemin, totaux d’octets et d’éléments.

Conservez une sauvegarde indépendante en utilisant cette fonction Alpha.

[Parcours CyStore](doc:cystore)
