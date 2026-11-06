#pragma once
#include <algorithm>
#include <string>

namespace cyrevision {
/**
 * @doc native-help-links
 * @locale en
 * @title Native contextual help
 * @section API
 * @symbol isLocalDocumentationLink
 * The managed host validates the page, section and media against the installed CyDoc manifest.
 *
 * ## Contract {#contract}
 * Native UI sends `docs.open` with `topicId`, optional `anchor` and `mediaId`.
 * The IPC `id` remains reserved for correlation. The returned file URL is opened by the UI host.
 * No HTTP server or arbitrary document-supplied shell scheme is used.
 */
/**
 * @doc native-help-links
 * @locale fr
 * @title Aide contextuelle native
 * @section API
 * @symbol isLocalDocumentationLink
 * Le moteur valide page, section et média contre le manifeste CyDoc installé.
 *
 * ## Contrat {#contract}
 * L’interface envoie `docs.open` avec `topicId`, puis `anchor` et `mediaId` facultatifs.
 * Le champ IPC `id` reste réservé à la corrélation. L’hôte ouvre l’URL fichier retournée.
 * Aucun serveur HTTP ni schéma arbitraire fourni par un document n’est utilisé.
 */
inline bool isLocalDocumentationLink(const std::string& url) {
    return url.rfind("file:///",0)==0 && url.find("/Documentation/Documentation.html#/")!=std::string::npos &&
        std::none_of(url.begin(),url.end(),[](unsigned char c){return c<32 || c==127;});
}
}
