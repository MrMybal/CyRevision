#include "Localization.h"
#include <cyui/Core.h>
namespace cyrevision {
void registerAppTranslations(){
    cyui::registerTranslations("fr",{
        {"Language","Langue"},{"Rendered","Rendu"},{"Source","Source"},
        {"Copy Markdown source","Copier la source Markdown"},{"Open source in a separate window","Ouvrir la source dans une fenêtre séparée"},
        {"Previous messages","Messages précédents"},{"Next messages","Messages suivants"},{"Attachment","Pièce jointe"},
        {"Conversation","Conversation"},{"Message details / attachments","Détails des messages / pièces jointes"},
        {"This link cannot be opened from a document.","Ce lien ne peut pas être ouvert depuis un document."},
        {"Open external link?","Ouvrir le lien externe ?"},{"Open in browser","Ouvrir dans le navigateur"},
        {"No image preview","Aucun aperçu d’image"},{"Choose an image or an asset with a rendered preview.","Choisissez une image ou une ressource disposant d’un aperçu."},
        {"Previous match (Shift+Enter)","Résultat précédent (Maj+Entrée)"},{"Next match (Enter)","Résultat suivant (Entrée)"},
        {"Apply text draft","Appliquer le brouillon"},{"Discard text draft","Annuler le brouillon"},
        {"Search files, folders, state or owner (substring)","Rechercher dans les fichiers, dossiers, états ou propriétaires"},
        {"Operating mode","Mode de fonctionnement"},{"Display name","Nom d’affichage"},{"Project color","Couleur du projet"},
        {"Project colour","Couleur du projet"},{"Group (existing or new)","Groupe (existant ou nouveau)"},
        {"Start Sync automatically","Démarrer Sync automatiquement"},{"Start VPN automatically","Démarrer le VPN automatiquement"},
        {"Find commit by message, author or hash","Rechercher un commit par message, auteur ou identifiant"},
        {"Find files / glob patterns","Rechercher des fichiers / motifs glob"},{"Show hidden files","Afficher les fichiers masqués"},
        {"Show diff preview","Afficher les différences"},{"Commits to analyze (10–1000)","Commits à analyser (10–1000)"},
        {"Files in relation graph (10–150)","Fichiers dans le graphe de relations (10–150)"},{"Include all branches","Inclure toutes les branches"},
        {"Provider","Fournisseur"},{"Model","Modèle"},{"API endpoint","Point d’accès API"},{"Session API key","Clé API de session"},
        {"New conversation title","Titre de la nouvelle conversation"},{"Render Markdown","Afficher le Markdown"},
        {"Conversation instructions","Instructions de la conversation"},{"Isolated worktree","Worktree isolé"},{"Message","Message"},
        {"Comment","Commentaire"},{"Description (Markdown)","Description (Markdown)"},{"Find assets","Rechercher des ressources"},
        {"Reference","Référence"},{"Candidate","Candidat"},{"Author","Auteur"},{"Title","Titre"},
        {"Refresh","Actualiser"},{"Fetch remotes","Récupérer les références distantes"},{"Pull","Tirer"},{"Push","Pousser"},
        {"Analyze repository","Analyser le dépôt"},{"Send","Envoyer"},{"Cancel turn","Annuler le tour"},
        {"Loading…","Chargement…"},{"File preview","Aperçu du fichier"},{"Preview truncated","Aperçu limité"},
        {"Previous change block","Bloc de modifications précédent"},{"Next change block","Bloc de modifications suivant"},
        {"Copy complete displayed text / patch","Copier le texte / patch affiché"},{"Open this view in a separate window","Ouvrir cette vue dans une fenêtre séparée"}
    });
}
}
