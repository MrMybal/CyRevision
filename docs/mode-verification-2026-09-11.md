# Corrections et vérification des modes — 11 septembre 2026

Code de départ : 64de457, v0.1.23, avec les modifications locales préexistantes de mise à jour et de badges Alpha/Beta. Ces modifications ont été conservées. Aucun commit, push ni publication de release n'a été effectué pendant cette intervention.

## Corrections

| Point de l'audit | Traitement |
| --- | --- |
| Sync + Commit écrasait les modifications locales non conflictuelles | Application d'un plan limité aux fichiers effectivement modifiés par l'incoming ; conservation des ajouts, modifications et suppressions locaux indépendants. |
| La sauvegarde ne couvrait pas les fichiers effectivement écrasés | Sauvegarde des chemins du plan avant écriture, inventaire des fichiers initialement absents, noms d'archives uniques et restauration en cas d'échec d'application. |
| Paquets incohérents acceptés | Vérification complète des entrées, tailles, hashes et manifeste avant application ; contrôle également des octets archivés à la publication. Les chemins dangereux, doublons et liens/jonctions sont refusés. |
| Git + Sync pouvait reculer une référence de pair | Séquence signée par auteur, dernier état importé persistant, rejet des annonces anciennes même reçues ultérieurement. Les nouvelles annonces de force-push et de suppression de branche restent prises en compte. Les branches de travail locales ne sont pas modifiées. |
| Bundles identiques publiés chaque minute | Pas de nouveau bundle si les références sont inchangées et que la publication existe encore. Les demandes et transferts LFS continuent indépendamment. Aucune ancienne transaction existante n'a été supprimée automatiquement. |
| Changement de mode sans reconfiguration du moteur | Arrêt du moteur avant changement, mise à jour du chemin d'échange, ancien partage retiré au démarrage suivant. Les dossiers indépendants restent dans le profil. Après application du mode, Sync est arrêté et doit être redémarré explicitement. |
| Projet en cache utilisant le moteur d'un autre projet | Détachement immédiat du moteur à la sélection ; contrôle de son propriétaire et du profil ; attente des arrêts en cours au redémarrage et à la fermeture. Les opérations de mode et d'échange gardent leur projet cible après les attentes asynchrones. |
| Adhésion impossible avec deux identifiants de catalogue différents | Identité partagée distincte de l'identité locale, associée uniquement après validation du certificat attendu. Conservation des vérifications de signature, appareil, émetteur, invitation et rôle. Les HEAD et caches d'avant l'adhésion sont conservés mais non réutilisés pour l'autre identité. |
| Serveur exécutant Sync + Commit comme Sync continu | **Restriction explicite** : Sync + Commit reste disponible sur desktop, mais n'est plus annoncé comme supporté par le serveur. Création/configuration/démarrage refusés pour ce mode côté serveur. Le serveur sauvegarde désormais le mode réel des autres projets. |
| Limite Backup ignorée en Timeline | Application du plafond de snapshots en Timeline comme en LimitedVersions ; Permanent reste sans purge par compteur. |
| Badge Alpha de Sync effacé | Mise à jour du texte uniquement, sans remplacer le conteneur du badge. |

Des vérifications supplémentaires ont aussi conduit à :

- utiliser la base commune pour appliquer une révision Sync après plusieurs commits manqués ;
- empêcher un HEAD d'un autre projet partagé de servir de parent à une nouvelle publication ;
- corriger la génération des nouveaux profils avec le runtime Syncthing fourni (--config et --data requis ensemble) ;
- reconstituer les membres autorisés lors du redémarrage d'un partage serveur.

## Tests automatisés

La suite a été étendue de **255 à 285 cas** : 30 nouveaux cas de non-régression, plus l'extension d'un test de persistance de profil.

Commande :

    dotnet test tests/CyRevision.Core.Tests/CyRevision.Core.Tests.csproj -c ModeAudit --no-restore --nologo --logger "trx;LogFileName=mode-fixes-verified.trx"

Dernière passe sur le code final : **285 tests réussis, 0 échec, 0 ignoré**, en 1 min 57 s. [Résultat TRX](../tests/CyRevision.Core.Tests/TestResults/mode-fixes-verified.trx).

Les tests couvrent notamment Git/Git LFS et le nettoyage protégé, les conflits et restaurations, les diffs et aperçus, les profils des modes, les sauvegardes, les plugins et leur isolation, CI/PR et tâches, les préférences/cache/mise à jour, la sécurité des pairs, VPN/chat/remote build. Pour les fournisseurs externes, il s'agit de tests de logique ou de services simulés, pas de connexions aux comptes réels.

Fichiers ajoutés :

- [SyncCommitRegressionTests.cs](../tests/CyRevision.Core.Tests/SyncCommitRegressionTests.cs)
- [GitPeerOrderingTests.cs](../tests/CyRevision.Core.Tests/GitPeerOrderingTests.cs)
- [ProjectModeRuntimeTests.cs](../tests/CyRevision.Core.Tests/ProjectModeRuntimeTests.cs)
- [PeerProjectAssociationTests.cs](../tests/CyRevision.Core.Tests/PeerProjectAssociationTests.cs)

Le test de génération du runtime Syncthing est exécuté sous Windows lorsque le binaire fourni est présent ; ailleurs, il est explicitement ignoré et aucun runtime n'est téléchargé.

## Essais réels locaux

Deux véritables instances du runtime Syncthing fourni ont été lancées avec des identités, profils, ports et répertoires synthétiques distincts. Elles se contactaient uniquement par des adresses loopback configurées explicitement ; découverte globale/locale, relais, NAT, télémétrie et mise à jour automatique étaient désactivés pour ces essais.

Résultats :

1. Sync continu : transfert puis modification d'un fichier — réussi.
2. Pause/reprise — réussies.
3. Passage de dossier continu à un échange de commits : anciens partages absents — réussi.
4. Aucune publication avant commit, transfert du paquet puis application explicite avec récupération — réussis.
5. Transmission d'un bundle Git signé via Syncthing puis import de la référence de pair sans checkout du destinataire — réussis.
6. Arrêt des deux moteurs et de leurs API — réussi.

Banc d'essai local execute hors du depot (non publie).

## Limites et livraison

- Aucun dépôt utilisateur n'a été nettoyé ni synchronisé pendant les essais ; les mutations ont concerné les fixtures synthétiques.
- La compilation et les tests utilisent ModeAudit pour ne pas remplacer les binaires verrouillés par l'application ouverte. L'agent de build a aussi compilé séparément sans erreur ni avertissement.
- Pas de validation exhaustive de tous les clics de l'interface, de réseau entre machines distinctes, de volumes de plusieurs téraoctets, ni des serveurs GitHub/Jira/ClickUp/Perforce/Lore réels. Ces essais restent nécessaires avant de promouvoir les modes Alpha.
- Les sauvegardes de récupération réduisent le risque d'échec d'application ; elles ne rendent pas un ensemble de fichiers atomique face à une coupure de courant.
- Ces résultats ne constituent pas une garantie d'absence de tout autre bug.

Binaire de vérification : [CyRevision.Desktop.exe](../src/CyRevision.Desktop/bin/ModeAudit/net8.0/CyRevision.Desktop.exe). L'instance déjà ouverte n'a pas été fermée ni relancée.
