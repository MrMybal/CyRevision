---
id: code-and-ai
title: Explorateur, recherche globale et permissions IA
description: Parcourir l'arborescence, chercher avec Ctrl+Shift+F, suivre l'historique d'une sélection et connecter un agent IA facultatif.
section: Parcours / Code et ressources
parent: code-explorer
order: 24
sourceRevision: 2
---

## Explorateur de code {#code-explorer}
L'onglet Code affiche l'arborescence hiérarchique, les métadonnées, l'aperçu texte et les symboles détectés. Les sorties de build, caches Unreal, .git et caches de dépendances sont exclus automatiquement.

## Recherche globale {#global-search}
Ctrl+Shift+F ouvre la recherche depuis toute l'application. La casse, les mots entiers, les expressions régulières et les motifs comme *.cs;*.cpp sont disponibles. ripgrep est utilisé s'il existe, sinon le moteur .NET prend le relais.

## Historique d'une sélection {#selection-history}
Sélectionnez des lignes puis cliquez History of selection. CyRevision suit cette plage inclusive dans Git. Un dossier affiche l'historique de tout son sous-arbre.

## Ia facultative {#optional-ai}
Activez AI Workspace dans Plugins. Codex CLI, l'API OpenAI Responses, une API compatible, Ollama et LM Studio sont proposés. La clé API reste en mémoire le temps de l'exécution.

## Permissions {#permissions}
La lecture est obligatoire. Modification, réseau, indexation et commit sont séparés. Codex reste en sandbox lecture seule tant que les modifications ne sont pas autorisées. CyRevision effectue lui-même l'indexation et le commit après succès. Aucun push n'est automatique.

## OpenCode et configuration du fournisseur {#opencode-and-provider-configuration}
Le sélecteur IA propose aussi OpenCode CLI. Choisissez OpenCode, réglez l'exécutable (opencode ou son emplacement installé) et le modèle/fournisseur attendu par votre CLI. Codex CLI est distinct : changer de fournisseur ne doit pas conserver le mauvais exécutable. API et modèles locaux exigent leur configuration compatible.

## Vérifier un tour {#review-a-turn}
Choisissez contexte projet et capacités, envoyez la demande, observez l'état et annulez si nécessaire. Examinez les diffs et testez avant d'appliquer/indexer/commiter. Un worktree isolé est un autre checkout, pas une autorisation de publier. Lisez l'état du fournisseur : tous les CLI externes n'imposent pas nécessairement le même sandbox.

```image
{"id":"ai","src":"assets/ai.fr.svg","alt":"Explorateur, recherche globale et permissions IA","controls":"minimal"}
```

[Voir aussi](doc:mcp-policy)
