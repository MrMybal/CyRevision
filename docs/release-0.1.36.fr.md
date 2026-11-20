# CyRevision 0.1.36 Alpha

[English](https://github.com/MrMybal/CyRevision/blob/v0.1.36/docs/release-0.1.36.md) · [Release et téléchargements](https://github.com/MrMybal/CyRevision/releases/tag/v0.1.36)

## Changements

- Actualisation de CyMCPSDK compilé depuis les sources : version 0.7.1, révision épinglée `34d4bcae126bc83d1a2925b5047dac36e3d09d92`.
- **Free** devient le profil d'accès partagé par défaut pour l'assistant, le CLI et MCP. Le client HTTP local utilise directement l'adresse affichée `http://127.0.0.1:PORT/mcp`, sans jeton ni confirmation supplémentaire des opérations.
- Maintien des profils **Local contrôlé**, **Jeton seul** et **Protégé**. Les configurations sans choix migrent vers Free ; les choix explicites par projet sont conservés.
- Séparation de l'authentification HTTP et des autorisations d'opérations. Tous les profils conservent schémas d'arguments, limites du projet, refus des cibles périmées, disponibilité des modes/plugins et blocage d'urgence.
- Blocage des anciens appels après changement de profil, jusqu'à son application par redémarrage du SDK ou démarrage du MCP local. Les ressources conservées suivent aussi la révocation du projet/profil.
- Connexion du CLI attaché avec une adresse loopback ou un descripteur d'instance, et signalement correct des jetons absents ou invalides.
- Actualisation du manuel intégré anglais/français : comparaison des profils, migration et exemples de connexion CLI/MCP.

## Paquets et prérequis

Cette release fournit un **installateur Windows x64**, un **ZIP portable** et `SHA256SUMS.txt`. Native V2, Classic, le compagnon CLI et le manuel hors ligne complet sont inclus. Le runtime .NET est fourni ; Git, Git LFS et les fournisseurs/services optionnels gardent leurs prérequis propres.

Le support source Linux/macOS est conservé. Leurs derniers paquets publiés restent en [0.1.34](https://github.com/MrMybal/CyRevision/releases/tag/v0.1.34) ; ils n'ont pas été reconstruits ou testés pour cette release.

## Limites documentées

- Free permet volontairement aux processus locaux d'appeler toutes les opérations exposées avec la seule adresse loopback. Choisissez Protégé pour exiger jeton et validation exacte, ou Local contrôlé pour la validation sans jeton.
- Les permissions shell/système propres au fournisseur et les réglages de comptes/exécutables restent séparés du registre applicatif. Free ne donne pas d'accès système arbitraire à un fournisseur.
- Après changement du profil actif, utilisez **Restart SDK runtime** ou **Start local MCP**, puis reconnectez les clients à la nouvelle adresse.
- Native V2, Sync, Git + Sync, Lore, sauvegardes et plugins restent Alpha ; les fonctions Git restent Bêta. Conservez des sauvegardes indépendantes et testez la restauration avant production.
- Les paquets Windows ne sont pas signés. Native V2 exige Windows 10/11 x64 et Direct3D 11.
- Comptes fournisseurs, CI de production et changements VPN/pare-feu exigent des validations propres à l'environnement. Les scénarios PR/CI et modèles utilisent des services simulés locaux ; une annulation ne défait pas une requête déjà acceptée par un service externe.
- La documentation en ligne est déployée séparément. Le lecteur actualisé est inclus dans `Documentation` et accessible depuis Aide/F1.

[Installation](https://github.com/MrMybal/CyRevision/blob/v0.1.36/README.fr.md#installation) · [Suivi des problèmes](https://github.com/MrMybal/CyRevision/issues)
