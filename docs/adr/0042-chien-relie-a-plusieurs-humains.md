# ADR 0042 — Un chien relié à plusieurs humains : liens `DogMember`, invitation par lien, droits égaux

- **Statut :** Proposé
- **Date :** 20/11/2026
- **Note :** décisions du porteur du projet ; à confirmer à l'implémentation.
- **Précise :** [ADR 0023](0023-suppression-de-compte.md) (suppression d'un chien à la suppression du compte) et [ADR 0037](0037-organisations-gelees-et-blocage-indiscernable.md) (plafond de 20 chiens et verrous sur les chiens).

## Contexte

Un chien n'a aujourd'hui qu'un seul propriétaire (`Dog.OwnerId`, clé étrangère en cascade ; `DogResponse.ownerId`). Cela ne correspond pas à la vie réelle : Victor a Uzu, sa femme Camille a Noux, mais les deux chiens sont à eux deux. Victor doit pouvoir emmener Noux à une balade sans Camille, sans que Noux existe en double sur deux comptes.

À terme, d'autres rôles se rattacheront au chien : une personne de confiance, un éducateur canin.

La V1 n'est pas déployée : il n'y a aucune donnée de production à reprendre.

Dans le code actuel, l'inscription (`Registration`) porte déjà `DogId` et `UserId` (l'humain qui amène le chien), avec un index unique partiel (`WalkId`, `DogId`) sur les statuts `Pending` et `Accepted` : un chien n'a qu'une inscription active par balade, quel que soit l'humain. Les notifications, le fil de messages et le blocage passent par `Registration.UserId`, et le blocage vise toujours un humain, jamais un chien.

## Décision

1. **Modèle.** Relation plusieurs-à-plusieurs entre `User` et `Dog` par une entité de lien `DogMember` (table `dog_members` : `DogId`, `UserId`, `Role`, `CreatedAt` ; clé unique (`DogId`, `UserId`)). `Dog.OwnerId` disparaît. Le rôle est une énumération `DogMemberRole`. En V1.x, un seul rôle existe : `Owner` (« Propriétaire »). `Trusted` (« Personne de confiance » : inscrit le chien aux balades sans pouvoir le modifier) et `Educator` (« Éducateur » : affiché sur le profil, sans droits) sont une évolution prévue (phase 2), non spécifiée en détail ; l'API ne les accepte pas encore.
2. **Droits égaux entre propriétaires.** Tout propriétaire modifie le chien, change sa photo, l'inscrit à une balade et invite un autre humain. Il n'y a pas de propriétaire « principal ».
3. **Invitation.** Un propriétaire crée une invitation pour un chien et un rôle (entité `DogInvitation`). Elle prend la forme d'un lien à partager (`uzu://invitation/{jeton}`), à usage unique, qui expire après 7 jours. La personne invitée doit l'accepter, connectée ; accepter crée le lien. Le jeton est aléatoire, seule son empreinte est gardée (comme les jetons de rafraîchissement, ADR 0035) et il n'est renvoyé qu'à la création. Points d'entrée : `POST /dogs/{id}/invitations`, `GET /dogs/{id}/invitations`, `DELETE /dogs/{id}/invitations/{invitationId}`, `GET /dog-invitations/{token}`, `POST /dog-invitations/{token}/accept`. Erreurs : 404 `invitation.not_found`, 409 `invitation.already_used`, 410 `invitation.expired`, 409 `dog.already_owner`, 409 `dog.limit_reached`. Une invitation appartient au chien : elle reste valable si son auteur quitte le chien ou supprime son compte (`createdById` devient nul).
4. **Quitter, supprimer.** Un propriétaire peut « ne plus s'occuper » du chien (`DELETE /dogs/{id}/members/me`, il supprime son lien) s'il reste au moins un autre propriétaire ; sinon 409 `dog.last_owner`. Supprimer le chien n'est possible que pour le dernier propriétaire ; sinon 409 `dog.has_other_owners`. Un propriétaire ne peut pas retirer un autre propriétaire (question ouverte ci-dessous). Quitter un chien ne touche pas aux inscriptions déjà faites.
5. **Suppression de compte.** Elle supprime le lien de l'utilisateur à chacun de ses chiens. Un chien n'est supprimé, avec ses effets actuels (inscriptions à venir annulées, photo effacée), que si l'utilisateur en était le dernier propriétaire.
6. **Inscription à une balade.** Tout propriétaire du chien peut l'inscrire ; `Registration.UserId` reste l'humain qui vient avec le chien. Un chien déjà inscrit (`Pending` ou `Accepted`) par un autre propriétaire ne peut pas l'être une seconde fois : la règle de doublon est inchangée, et le client affiche « Déjà inscrit par {prénom} ». Les inscriptions `Pending` deviennent visibles des propriétaires du chien, pour que ce message soit possible. Notifications, fil de messages et blocage continuent de viser l'humain de l'inscription. Conséquence assumée : si un organisateur a bloqué Camille, Victor peut quand même inscrire Noux.
7. **Annulation d'une inscription.** L'auteur de l'inscription ou tout propriétaire du chien peut l'annuler. Quand l'annulation vient d'un autre propriétaire, l'auteur de l'inscription reçoit une notification (préférence `registrations`) : il ne doit pas venir à la balade en croyant le chien inscrit. Décision du porteur du projet.
8. **Limite de 20 chiens.** Elle se compte par humain, chiens partagés compris, sur ses liens `DogMember`. Elle s'applique à la création d'un chien et à l'acceptation d'une invitation.
9. **Contrat d'API.** `DogResponse` remplace `ownerId` par `members` : liste de `{ user: UserSummaryResponse, role }`, du lien le plus ancien au plus récent. `GET /me/dogs` et le profil public (`GET /users/{id}`) renvoient les chiens dont l'humain est propriétaire. `ReportDogResponse` (administration) remplace aussi `ownerId` par `members`, pour que l'administrateur voie tous les humains d'un chien signalé.

**Affichage mobile.** Le profil du chien montre :

- un en-tête : la photo du chien avec les avatars de ses humains accrochés en bas à droite, son nom, sa race, et une ligne « avec Camille et Victor » (ou « avec Camille et toi ») ;
- puis, dans cet ordre, « Son caractère », « Description » et « Ses humains » : une ligne par humain avec avatar, nom et pastille de rôle, cliquable vers son profil ; pour un propriétaire, une ligne « Ajouter un humain » en fin de carte, qui crée l'invitation et ouvre le partage du lien ;
- enfin les boutons « Modifier » et « Ne plus m'occuper de {nom} » (ou « Supprimer » pour le dernier propriétaire).

Sur le profil d'un humain, la carte d'un chien partagé affiche « aussi avec {prénom} » avec le petit avatar.

**Reporté.** Une galerie de photos du chien façon Instagram (phase 3) ; le détail des rôles `Trusted` et `Educator` (phase 2).

## Conséquences

- **Migration.** `dogs.owner_id` est remplacé par la table `dog_members`. Comme la V1 n'est pas déployée, la migration n'a pas de données de production à reprendre ; elle reprend toutefois les bases locales (voir « Précisions de l'implémentation »). Un chien créé reçoit son lien `Owner`. Les données de démonstration (`DemoDataBuilder`, `DemoDataService`) et les tests qui lisent `OwnerId` sont à adapter.
- **Autorisations.** `FindOwnedDogAsync` vérifie un lien `Owner` au lieu de `OwnerId` ; le filtre d'inscription (`RegistrationRules`) fait de même ; l'annulation d'une inscription accepte son auteur ou un propriétaire du chien.
- **ADR 0037 précisé.** Le plafond de 20 se compte désormais sur les liens de l'humain, pas sur les chiens qu'il a créés. Créer un chien et accepter une invitation verrouillent donc la ligne du compte pour que le plafond tienne. L'ordre des verrous reste : compte, chiens, puis balades. Ne plus s'occuper d'un chien, le supprimer et accepter une invitation verrouillent le chien (`FOR UPDATE`) pour que l'invariant « au moins un propriétaire » tienne quand deux propriétaires partent en même temps.
- **ADR 0023 précisé.** La suppression de compte supprime des liens, et ne supprime un chien que si l'utilisateur en était le dernier propriétaire. Ses inscriptions sont supprimées comme avant, y compris celles qu'il avait faites pour un chien partagé : l'autre propriétaire peut réinscrire le chien. Les invitations qu'il a créées ou acceptées sont détachées (`createdById` et `usedById` nuls) plutôt que supprimées.
- **Blocage contournable par un chien partagé.** Un humain bloqué ne peut pas s'inscrire, mais un autre propriétaire de son chien peut l'inscrire. C'est assumé : le blocage vise une personne, pas un chien.
- **Compte suspendu.** Ses liens sont conservés, les autres propriétaires peuvent continuer à inscrire le chien.
- **Spécification et contrat.** `docs/specification-v1.md` passe en version 0.3 ; `api/openapi.json` et le client mobile devront être régénérés à l'implémentation.

## Précisions de l'implémentation (API)

- **Migration.** La sixième migration (`AddDogMembersAndInvitations`) crée `dog_members` et `dog_invitations`, copie chaque `dogs.owner_id` en lien `Owner` daté comme le chien, puis supprime `owner_id`, son index et sa clé étrangère : les bases locales existantes sont reprises. Le retour arrière remet le propriétaire du lien le plus ancien.
- **Jeton.** 32 octets d'un générateur cryptographique, encodés en base64url sans remplissage (43 caractères) ; empreinte SHA-256 en hexadécimal, index unique. Un jeton mal formé répond `invitation.not_found`. Réponse de création : `{ id, dogId, role, token, link, expiresAt, createdAt }`.
- **Verrous.** Accepter prend la ligne du compte (`FOR UPDATE`, plafond), puis celle du chien, puis celle de l'invitation : supprimer un chien verrouille le chien avant que ses invitations ne partent en cascade, l'invitation n'est donc jamais verrouillée en premier. La suppression de compte verrouille ses chiens, par identifiant croissant, juste après la ligne du compte et avant les balades.
- **Limitation de débit.** La création d'invitation, la consultation et l'acceptation par jeton comptent comme écritures sensibles (ADR 0031) ; la consultation est une lecture, mais elle prend un jeton : la règle « sensible » ne regarde plus la méthode, seulement le nom du point d'entrée.
- **Fiche d'une balade.** `WalkDetailResponse.myDogsRegistrations` liste les inscriptions `Pending` et `Accepted` des chiens dont l'utilisateur est propriétaire, quel que soit leur auteur : `{ id, dogId, status, registeredBy }`. `myRegistrations` est inchangé. `GET /me/walks?role=Participant` reste fondé sur `Registration.userId` : un chien inscrit par un autre propriétaire n'y figure pas.
- **Notification.** Type `registration_cancelled_by_owner`, préférence `registrations` : « Inscription annulée » / « {nom} a annulé l'inscription de {chien} à « {titre} ». ».
- **Données de démonstration.** Romain Gasser et Laura Petit partagent Oscar ; Laura l'inscrit à « Cani-balade au lever du soleil ».

## Précisions de l'implémentation (mobile)

- **Lien d'invitation avant la connexion.** Le lien `uzu://invitation/{jeton}` est intercepté par `src/app/+native-intent.ts` (Expo Router), au démarrage comme application ouverte. Le jeton est gardé en mémoire seulement (jamais écrit sur l'appareil, affiché ni journalisé), puis l'écran de l'invitation s'ouvre dès que la personne est connectée. Pour un nouveau compte, l'invitation passe juste après le profil, avant « Ajoute ton premier chien ». Le lien sert une fois et s'efface à la fin d'une session ; il est perdu si le système tue l'application pendant la connexion. Ce mécanisme est distinct de celui des notifications, qui jette au contraire un appui reçu déconnecté pour ne pas le livrer à un autre compte.

## Questions ouvertes

- **Retirer un autre propriétaire.** Aujourd'hui seul un propriétaire peut se retirer lui-même. Faut-il permettre à un propriétaire d'en retirer un autre (séparation, invitation à la mauvaise personne), et sous quelle protection contre un retrait abusif ?
- **Rôles de phase 2.** Droits exacts de `Trusted` et `Educator`, qui peut les attribuer, affichage sur le profil, et effet sur les règles d'inscription et de plafond.
- **Inscriptions d'un humain qui part.** Elles sont conservées quand un propriétaire ne s'occupe plus du chien ; à revoir si cela surprend à l'usage.
- **Limite d'invitations.** Aucun plafond d'invitations en attente par chien ; à ajouter seulement si un abus apparaît (la limitation de débit de l'ADR 0031 s'applique déjà).

## Alternatives écartées

- **Dupliquer le chien sur chaque compte.** Noux existerait en double : deux profils à tenir à jour, deux inscriptions possibles à la même balade pour le même animal, et la capacité d'une balade comptée deux fois. La règle « un chien, une inscription active par balade » n'aurait plus de sens.
- **Propriétaire principal et co-propriétaires aux droits réduits.** Il faudrait désigner un « principal », gérer son départ ou la transmission, et expliquer pourquoi Camille, propriétaire de Noux, aurait moins de droits que Victor. Cela ne correspond pas au cas d'usage, et les droits réduits sont ce que le futur rôle `Trusted` apportera, pour de bon.
- **Simple champ `coOwnerId` sur le chien.** Limité à deux humains, sans place pour un rôle ni pour un éducateur, et à remplacer dès la phase 2. La table de liaison coûte à peine plus cher dès maintenant.
