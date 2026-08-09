# Explorateur Git et Time Machine LFS

## Explorateur Git

L'onglet **History** est un explorateur en lecture seule. Il permet de rechercher un commit par message, auteur ou hash, puis d'afficher ses fichiers, leurs statistiques et un diff ciblé. La sélection d'un nœud dans le réseau de commits charge le même inspecteur.

Deux révisions peuvent être comparées avec **Compare A ↔ B**. La liste centrale devient alors la liste des fichiers différents entre la base et la cible. Sélectionner un fichier affiche son diff et son propre historique. **Export this version** écrit une copie à l'emplacement choisi sans toucher au working tree.

## Time Machine Git LFS

La Time Machine liste les fichiers LFS de `HEAD`, puis les objets uniques rencontrés dans leur historique. Chaque version affiche :

- le commit et sa date ;
- la taille déclarée par le pointeur LFS ;
- l'OID SHA-256 ;
- les emplacements connus : **Local**, **Peer**, **Archive** ou **Missing**.

Les textures locales prises en charge peuvent être prévisualisées sans Unreal. L'export copie l'objet LFS réel, pas son petit fichier pointeur. Une version manquante n'est jamais téléchargée silencieusement. Si un inventaire signé indique qu'un pair autorisé la possède, **Request from peer** crée une demande signée ; le pair la publie à son prochain échange, puis le transfert peut reprendre après une interruption et n'est accepté qu'après vérification SHA-256.

La restauration nécessite une confirmation. Elle remplace uniquement le fichier du working tree ; elle ne crée ni indexation, ni commit, ni échange réseau. Git permet donc de contrôler la modification avant de la conserver ou de l'annuler.
