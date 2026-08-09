# Localisation de l’interface

CyRevision démarre en anglais lors du premier lancement. Le français est inclus et le sélecteur de langue est disponible directement dans l’en-tête. Le choix est mémorisé indépendamment sur chaque appareil.

## Client desktop

Les catalogues fournis se trouvent dans `src/CyRevision.Desktop/Localization/Locales`. Au démarrage, ils sont complétés ou remplacés par les fichiers JSON placés dans :

- Windows : `%APPDATA%\CyRevision\locales` ;
- Linux : `~/.config/CyRevision/locales` ;
- macOS : le dossier de configuration applicatif `CyRevision/locales`.

Pour ajouter une langue, copiez `en.json` sous un code ISO court, par exemple `de.json`, remplacez `$name` par le nom natif affiché dans le sélecteur puis traduisez les valeurs. Les clés doivent rester inchangées. Un fichier utilisateur peut ne contenir que les traductions à corriger : il est fusionné avec le catalogue embarqué du même code.

La section optionnelle `$patterns` traduit les messages contenant des nombres ou des valeurs variables. Ses clés sont des expressions régulières et les valeurs peuvent réutiliser les groupes `$1`, `$2`, etc. Un motif invalide ou trop lent est ignoré afin de ne jamais empêcher le démarrage de CyRevision.

Le code de la langue choisie est enregistré dans `ui-language.txt`. Si ce fichier ou un catalogue est inaccessible, CyRevision continue en anglais.

## Tableau de bord serveur

Le dashboard web utilise les catalogues `src/CyRevision.Server/wwwroot/locales/en.json` et `fr.json`. Les clés sont sémantiques (`project.createTitle`, par exemple), ce qui facilite l’ajout d’un nouveau fichier de langue. Ajoutez ensuite son code à la liste `supported` dans `wwwroot/app.js` et une option au sélecteur de `index.html`.

Le navigateur mémorise le choix localement. Aucune préférence de langue n’est envoyée au serveur et aucun service Git, Sync ou VPN n’est redémarré.
