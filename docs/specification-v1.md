# Uzu — Spécification technique V1

**Version :** 0.3 · 20/11/2026

Ce document traduit le [périmètre V1](perimetre-v1.md) en modèle de données, règles et points d'entrée. Il sert de contrat entre l'API et l'application mobile. Les identifiants de code sont en anglais ; les textes affichés sont en français.

Les choix faits ici sans validation explicite sont listés à la fin, section « Choix faits en autonomie ». La forme exacte des réponses (champs, types, codes de statut) est celle de [`api/openapi.json`](../api/openapi.json), qui fait foi ; ce document en donne les règles.

La version 0.2 reporte les décisions de la revue de la V1 (ADR 0031 à 0037). Ces ADR sont « Proposé » tant que le porteur du projet ne les a pas validés.

La version 0.3 permet de relier un chien à plusieurs humains (ADR 0042, « Proposé » lui aussi) : `Dog.ownerId` disparaît au profit des liens `DogMember`, avec invitation par lien. Les sections touchées sont le modèle (§3), les règles des chiens (§4.1), des inscriptions (§4.3), des notifications (§4.4), du blocage (§4.7), de la suppression de compte (§4.8) et les points d'entrée du lot 1 (§5).

## 1. Conventions de l'API

- **Base :** `/api/v1`. JSON en camelCase. Les énumérations sont sérialisées en chaînes et acceptées seulement sous leur nom, jamais sous leur valeur entière.
- **Identifiants :** GUID. **Dates :** ISO 8601 ; en sortie en UTC, en entrée avec un décalage explicite obligatoire (`Z` ou `±hh:mm`).
- **Authentification :** jeton d'accès `Bearer` (JWT, 15 minutes) et jeton de rafraîchissement (30 jours, renouvelé à chaque usage, stocké haché). Tout exige une authentification, sauf `/auth/*`, `/health`, `/dev/demo-data` (développement seulement, ADR 0027) et les fichiers de photos.
- **Jeton de rafraîchissement :** un jeton remplacé reste accepté 60 secondes et donne alors une nouvelle session de la même famille ; au-delà, le présenter de nouveau révoque la famille de jetons issue de la même connexion, pas toutes les sessions de la personne. La déconnexion révoque la famille (ADR 0035).
- **Erreurs :** `application/problem+json` (RFC 9457), avec une extension `code` stable, par exemple `walk.full`. Les erreurs de validation listent les champs fautifs.
- **Photos :** `photoUrl` est un chemin relatif à l'origine du serveur (`/photos/...`), servi sans authentification sous un nom aléatoire ; voir l'ADR 0010. Seuls le JPEG, le PNG et le WebP sont acceptés (le HEIC est refusé). Le serveur réencode l'image et en retire toutes les métadonnées, dont la position GPS ; il refuse plus de 25 mégapixels et réduit à 2 048 pixels le grand côté. Le fichier est servi avec un cache privé d'un jour (ADR 0032).
- **Listes :** paramètre `limit` (50 par défaut, 100 au plus). Le fil de messages se pagine avec `before`.
- **Compte suspendu :** toute écriture renvoie 403 avec le code `account.suspended`, sauf `DELETE /me` (ADR 0036).
- **Limites (ADR 0031) :**
  - la limitation de débit répond 429 `rate_limit.exceeded` avec un en-tête `Retry-After` ;
  - un corps de requête fait 256 Ko au plus, hors photos ; au-delà, 413 `request.too_large` ;
  - un jeton d'appareil doit avoir la forme d'un jeton Expo ;
  - un compte garde dix appareils au plus : l'enregistrement d'un onzième remplace le plus ancien.
- **Environnements (ADR 0031) :** la connexion de test et les données de démonstration ne sont permises qu'en `Development` et `Testing`. Tout autre nom d'environnement est traité comme la production, où un compte de test n'est jamais administrateur.

## 2. Énumérations

| Énumération | Valeurs | Libellés affichés |
| --- | --- | --- |
| `DogSize` | `Small`, `Medium`, `Large` | Petit, Moyen, Grand |
| `Sociability` | `Easygoing`, `Selective`, `Reserved` | À l'aise, Sélectif, Réservé |
| `Reactivity` | `None`, `Mild`, `Strong` | Aucune, Légère, Marquée |
| `LeashHabit` | `OnLeash`, `LongLine`, `OffLeash` | En laisse, En longe, En liberté |
| `LeashRule` | `LeashRequired`, `OffLeashAllowed` | Laisse obligatoire, Liberté possible |
| `WalkAccess` | `Open`, `Approval` | Ouverte, Sur approbation |
| `WalkStatus` | `Scheduled`, `Cancelled` | Prévue, Annulée |
| `RegistrationStatus` | `Pending`, `Accepted`, `Declined`, `Cancelled` | En attente, Acceptée, Refusée, Annulée |
| `OrganizationStatus` | `Pending`, `Approved`, `Rejected`, `Closed` | En attente, Validée, Refusée, Fermée |
| `DogMemberRole` | `Owner` | Propriétaire |
| `MembershipRole` | `Owner`, `Educator`, `Member` | Responsable, Éducateur, Membre |
| `MembershipStatus` | `Pending`, `Accepted`, `Declined` | En attente, Accepté, Refusé |
| `ReportType` | `IncidentNoInjury`, `Altercation`, `BiteOrInjury`, `HumanBehavior` | Incident sans blessure, Altercation, Morsure ou blessure, Comportement d'un humain |
| `ReportStatus` | `Open`, `Reviewed`, `Closed` | Ouvert, Examiné, Clos |
| `UserStatus` | `Active`, `Suspended` | Actif, Suspendu |

## 3. Modèle de données

| Entité | Lot | Champs |
| --- | --- | --- |
| `User` | 1 | `id`, `provider` (`Apple`, `Google`, `Dev`), `providerSubject`, `email` (privé, facultatif), `displayName`, `bio`, `photoUrl`, `status`, `createdAt` |
| `RefreshToken` | 1 | `id`, `userId`, `familyId`, `tokenHash`, `expiresAt`, `revokedAt`, `replacedAt` |
| `Dog` | 1 | `id`, `name`, `photoUrl`, `size`, `birthDate` (facultatif), `breed` (facultatif), `sociability`, `reactivity`, `leashHabit`, `description`, `createdAt` |
| `DogMember` | 1 | `dogId`, `userId`, `role` (`DogMemberRole`), `createdAt` ; clé unique (`dogId`, `userId`) ; supprimé avec le chien ou avec l'utilisateur |
| `DogInvitation` | 1 | `id`, `dogId`, `createdById` (nul si compte supprimé), `role`, `tokenHash`, `expiresAt`, `usedAt`, `usedById` (nul si compte supprimé), `revokedAt`, `createdAt` ; supprimée avec le chien |
| `Walk` | 2 | `id`, `organizerId`, `title`, `description`, `startsAt`, `durationMinutes`, `location` (point géographique), `locationLabel`, `maxDogs`, `allowedSizes` (au moins une), `leashRule`, `rules`, `access`, `status`, `organizationId` (lot 4), `seriesId` (lot 3), `reminderSentAt` (interne, non exposé), `createdAt` |
| `Registration` | 2 | `id`, `walkId`, `dogId`, `userId`, `status`, `createdAt` |
| `WalkSeries` | 3 | `id`, `organizerId`, `timeZone` (`Europe/Paris`), `active`, `createdAt` |
| `Message` | 3 | `id`, `walkId`, `authorId` (nul si compte supprimé), `text` (1 000 caractères au plus), `createdAt` |
| `Device` | 3 | `id`, `userId`, `expoPushToken`, `platform`, `updatedAt` |
| `NotificationPreferences` | 3 | `userId`, `registrations`, `walkChanges`, `messages`, `reminders` (booléens, vrais par défaut) |
| `Organization` | 4 | `id`, `name`, `description`, `city`, `status`, `createdById`, `createdAt` |
| `Membership` | 4 | `id`, `organizationId`, `userId`, `role`, `status`, `createdAt` |
| `Block` | 4 | `id`, `blockerId`, `blockedId`, `createdAt` |
| `Report` | 4 | `id`, `reporterId` (nul si compte supprimé), `targetUserId`, `targetDogId`, `walkId` (tous facultatifs, au moins une cible), `type`, `facts`, `status`, `adminNote`, `createdAt` |

`MeResponse` (réponse de `GET /me`) contient en plus `email` et `isAdmin`, visibles de la personne elle-même seulement. `DogResponse` remplace `ownerId` par `members` : la liste des `{ user: UserSummaryResponse, role }`, du lien le plus ancien au plus récent. `ReportDogResponse` (administration) remplace de même `ownerId` par `members`. `RefreshToken.familyId` est expliqué par l'ADR 0035 et `Walk.reminderSentAt` par la règle du rappel (§4.4).

## 4. Règles métier

### 4.1 Profils

- Un utilisateur ne modifie que son profil et les chiens dont il est propriétaire.
- Le profil public d'un utilisateur montre `displayName`, `bio`, `photoUrl` et ses chiens, c'est-à-dire ceux dont il est propriétaire, chacun avec ses humains. L'e-mail n'est jamais exposé aux autres ; `GET /me` le renvoie à la personne elle-même, avec `isAdmin`.
- Un chien est relié à un ou plusieurs humains par des liens `DogMember` (ADR 0042). Le créateur d'un chien en devient propriétaire. En V1.x, le seul rôle est `Owner` (« Propriétaire »). Les rôles `Trusted` (« Personne de confiance ») et `Educator` (« Éducateur ») sont prévus pour la phase 2 et ne sont pas encore acceptés par l'API.
- Les propriétaires d'un chien ont tous les mêmes droits : modifier le chien, changer sa photo, l'inscrire à une balade, inviter un autre humain. Il n'y a pas de propriétaire « principal ». `GET /me/dogs` renvoie les chiens dont l'utilisateur est propriétaire.
- Un humain a 20 chiens au plus, chiens partagés compris : le plafond se compte sur ses liens, à la création d'un chien comme à l'acceptation d'une invitation. Au-delà, `dog.limit_reached` (ADR 0037, 0042).
- Supprimer un chien est réservé à son dernier propriétaire ; cela annule ses inscriptions à venir (quel que soit l'humain qui les a faites) et efface sa photo. Tant qu'un autre propriétaire reste, on ne peut que ne plus s'occuper du chien (voir plus bas).
- La taille d'un chien ne peut pas changer tant qu'il est inscrit (`Pending` ou `Accepted`) à une balade à venir qui n'admet pas la nouvelle taille : code `dog.size_in_use`.

**Inviter un humain.** Un propriétaire crée une invitation pour un chien et un rôle (`Owner` seulement en V1.x).

- L'invitation prend la forme d'un lien `uzu://invitation/{jeton}` que le propriétaire partage comme il veut. Le jeton est aléatoire ; le serveur n'en garde que l'empreinte, et ne le renvoie qu'à la création.
- Elle est à usage unique et expire 7 jours après sa création. Un propriétaire peut la révoquer avant.
- La personne invitée doit être connectée et accepter : accepter crée son lien avec le chien. Ouvrir le lien ne donne aucun droit ; la consultation montre seulement le chien, l'invitant et le rôle proposé.
- Une invitation reste valable si son auteur ne s'occupe plus du chien ou supprime son compte : elle appartient au chien.

| Règle | Code d'erreur |
| --- | --- |
| Le jeton correspond à une invitation existante et non révoquée | 404 `invitation.not_found` |
| L'invitation n'a pas déjà servi | 409 `invitation.already_used` |
| L'invitation n'est pas expirée | 410 `invitation.expired` |
| L'utilisateur n'est pas déjà propriétaire du chien | 409 `dog.already_owner` |
| L'utilisateur a moins de 20 chiens | 409 `dog.limit_reached` |

Les règles se contrôlent dans cet ordre à l'acceptation ; la consultation ne contrôle que les trois premières. Créer ou révoquer une invitation est réservé aux propriétaires du chien (403 `dog.forbidden` sinon, 404 si le chien n'existe pas), et un rôle autre que `Owner` est refusé comme une erreur de validation du champ `role`.

Précisions de l'implémentation : le jeton compte 256 bits aléatoires, encodés en base64url sans remplissage (43 caractères) ; le serveur en garde l'empreinte SHA-256. Un jeton mal formé répond comme un jeton inconnu (`invitation.not_found`). Révoquer est idempotent ; révoquer une invitation déjà acceptée répond 409 `invitation.already_used`, une invitation inconnue de ce chien 404 `invitation.not_found`. La consultation et l'acceptation par jeton suivent la limite de débit des écritures sensibles, même la consultation qui est une lecture (ADR 0031, 0042).

**Ne plus s'occuper d'un chien.** Un propriétaire supprime son propre lien avec le chien, à condition qu'il reste au moins un autre propriétaire ; sinon 409 `dog.last_owner` (il peut alors seulement supprimer le chien). Supprimer le chien alors qu'un autre propriétaire reste est refusé : 409 `dog.has_other_owners`. Un propriétaire ne peut pas retirer un autre propriétaire (question ouverte, ADR 0042). Quitter un chien ne touche pas aux inscriptions déjà faites : elles restent à l'humain qui les a créées. Quelqu'un qui n'est pas propriétaire du chien reçoit 403 `dog.forbidden`.

### 4.2 Balades

- Seul l'organisateur modifie ou annule une balade, et seulement avant son début.
- Le titre compte de 3 à 80 caractères, espaces de début et de fin exclus.
- Une balade commence au plus un an après sa création ou sa modification. *Règle ajoutée par la revue, à confirmer par le porteur du projet.*
- `maxDogs` ne peut pas descendre sous le nombre de chiens déjà acceptés : code `walk.capacity_below_registered`.
- Annuler une balade la passe à `Cancelled` et prévient les inscrits.
- La recherche renvoie les balades `Scheduled` à venir, dans un rayon autour d'un point, filtrables par période et par gabarit admis. Elle indique pour chacune la distance, le nombre de places prises et si elle est professionnelle.
- Le point de rendez-vous exact est visible par tous.
- Retirer un gabarit admis qu'un chien accepté possède est refusé : code `walk.size_in_use`.

### 4.3 Inscriptions

Une inscription concerne un chien. On peut inscrire plusieurs de ses chiens en une demande ; elle réussit ou échoue en bloc. Tout propriétaire du chien peut l'inscrire ; `Registration.userId` est l'humain qui vient avec le chien (ADR 0042).

| Règle | Code d'erreur |
| --- | --- |
| La balade est `Scheduled`, n'a pas commencé, et son organisation (s'il y en a une) est `Approved` | `walk.not_open` |
| L'utilisateur est propriétaire du chien | 404 |
| Le gabarit du chien est admis | `walk.size_not_allowed` |
| Le chien n'a pas déjà une inscription `Pending` ou `Accepted`, quel que soit l'humain qui l'a faite | `registration.duplicate` |
| Il reste assez de places (seules les inscriptions `Accepted` comptent) et l'organisateur n'a pas bloqué l'utilisateur | `walk.full` |
| Balade professionnelle : l'utilisateur est membre accepté de l'organisation | `walk.members_only` |

- Accès `Open` : l'inscription est `Accepted` tout de suite. Accès `Approval` : elle est `Pending` jusqu'à la décision de l'organisateur.
- Le blocage par l'organisateur répond exactement comme une balade complète (`walk.full`) : la personne bloquée ne peut pas le deviner (ADR 0037).
- L'acceptation revérifie le statut de l'organisation, le gabarit, la capacité et l'adhésion.
- Sur une balade `Approval` déjà complète, une nouvelle demande est refusée avec `walk.full`.
- L'organisateur peut inscrire ses propres chiens ; ils sont acceptés d'office et comptent dans la capacité.
- Un chien déjà inscrit par un autre propriétaire ne peut pas l'être une seconde fois (`registration.duplicate`) ; le client affiche « Déjà inscrit par {prénom} ».
- L'auteur de l'inscription ou tout propriétaire du chien l'annule avant le début ; la place est libérée.
- L'organisateur peut refuser ou retirer un participant ; l'inscription passe à `Declined`.
- La fiche d'une balade montre les chiens `Accepted` avec leur profil et l'humain qui vient avec eux. Les inscriptions `Pending` ne sont visibles que de l'organisateur, de leur auteur et des propriétaires du chien.
- La fiche d'une balade (`WalkDetailResponse`) donne à chacun, en plus de `myRegistrations` (ses propres inscriptions, tout statut), `myDogsRegistrations` : les inscriptions `Pending` et `Accepted` des chiens dont il est propriétaire, quel que soit l'humain qui les a faites, de la plus ancienne à la plus récente. Chaque élément vaut `{ id, dogId, status, registeredBy }`, où `registeredBy` est l'humain de l'inscription (`UserSummaryResponse`). C'est ce qui permet d'afficher « Déjà inscrit par {prénom} » et d'annuler l'inscription (`DELETE /walks/{id}/registrations/{id}`).
- Le blocage, les notifications et le fil de messages visent l'humain de l'inscription (`Registration.userId`), jamais le chien ni ses autres propriétaires. Conséquence assumée : si un organisateur a bloqué Camille, Victor peut quand même inscrire Noux, leur chien commun.

### 4.4 Fil de balade et notifications

- Seuls l'organisateur et les humains des inscriptions `Accepted` (ceux qui viennent avec un chien) lisent et écrivent dans le fil. Un autre propriétaire du chien n'y a pas accès.
- Les notifications sont envoyées par le service d'Expo, derrière une interface. Chaque type respecte les préférences du destinataire.

| Événement | Destinataires | Préférence |
| --- | --- | --- |
| Nouvelle inscription ou demande | Organisateur | `registrations` |
| Inscription acceptée ou refusée | Auteur de l'inscription | `registrations` |
| Inscription annulée par un autre propriétaire du chien (`registration_cancelled_by_owner`) | Auteur de l'inscription | `registrations` |
| Balade modifiée ou annulée | Inscrits `Accepted` et `Pending` | `walkChanges` |
| Nouveau message | Organisateur et inscrits `Accepted`, sauf l'auteur | `messages` |
| Rappel, 2 heures avant le début | Organisateur et inscrits `Accepted` | `reminders` |
| Prochaine balade d'une série créée | Inscrits `Accepted` de la précédente | `walkChanges` |
| Signalement créé (`report_created`) | Administrateurs, sauf le signalant | Aucune : toujours envoyée |
| Demande d'adhésion reçue, adhésion acceptée, organisation validée | Personnes concernées | `registrations` |

- Dans ce tableau, « inscrits » et « auteur de l'inscription » désignent l'humain de l'inscription (`Registration.userId`) ; les autres propriétaires du chien ne sont pas prévenus. Quand un propriétaire annule l'inscription faite par un autre humain, l'auteur de l'inscription est prévenu.
- Un rappel est envoyé une fois par heure de début, même si le serveur redémarre : déplacer une balade après l'envoi du rappel en déclenche un nouveau.
- Les notifications sont envoyées au mieux : une notification en file est perdue si le serveur s'arrête, et un rappel n'est jamais envoyé deux fois (ADR 0019).
- Écrire dans le fil d'une balade annulée est refusé (`walk.not_open`) ; la lecture reste possible.

### 4.5 Récurrence

- Une balade créée avec `repeatWeekly` appartient à une série.
- Quand une balade de série commence, la suivante est créée sept jours plus tard à la même heure locale (`Europe/Paris`, changements d'heure compris). Il n'existe jamais plus d'une balade à venir par série.
- Les inscriptions ne sont pas reportées ; les inscrits de la précédente sont prévenus.
- L'organisateur peut arrêter la série. Annuler une balade n'arrête pas la série.
- La balade suivante reprend l'heure locale de la dernière balade de la série : modifier l'heure d'une balade décale les suivantes.

### 4.6 Organisations

- Tout utilisateur peut demander la création d'une organisation. Elle est `Pending` jusqu'à validation par un administrateur ; son créateur en est `Owner`.
- Seule une organisation `Approved` est visible des autres utilisateurs et peut porter des balades.
- Un utilisateur demande à rejoindre ; un `Owner` ou un `Educator` accepte ou refuse. L'`Owner` peut nommer un membre `Educator`.
- Une balade professionnelle porte un `organizationId`. Seuls l'`Owner` et les `Educator` d'une organisation `Approved` en créent. Elle est visible de tous et marquée comme professionnelle, mais l'inscription est réservée aux membres acceptés.
- La liste des membres n'est visible que de l'`Owner` et des `Educator`.
- Un utilisateur ne peut pas avoir plus de trois organisations `Pending` à la fois. Le seul `Owner` ne peut pas quitter son organisation.
- Une balade dont l'organisation n'est plus `Approved` (fermée ou refusée) est gelée : elle reste, avec ses inscriptions, mais n'en accepte plus (`walk.not_open`) ; l'organisateur peut l'annuler. Sa série s'arrête à la prochaine échéance. Une série s'arrête aussi quand son organisateur n'est plus responsable ni éducateur accepté de l'organisation.
- Quitter une organisation ou en être retiré annule les inscriptions à venir de la personne aux balades de cette organisation, sauf à celles qu'elle organise, sans notification (ADR 0037).

### 4.7 Blocage, signalement, administration

- Bloquer une personne l'empêche de s'inscrire aux balades que j'organise ; le blocage vise l'humain, pas ses chiens : un chien qu'elle partage peut être inscrit par un autre de ses propriétaires (§4.3). Elle n'est pas prévenue et la réponse est exactement celle d'une balade complète (`walk.full`, §4.3).
- La fiche et la liste des balades indiquent `hasBlockedParticipant` quand l'organisateur ou un inscrit `Accepted` fait partie des personnes que j'ai bloquées.
- Un signalement vise une personne, un chien ou une balade. Il n'est visible que des administrateurs. À sa création, les administrateurs reçoivent une notification push qui ne donne que la catégorie, jamais les faits ni les personnes, et le signalement est journalisé (ADR 0036). Aucun e-mail n'est envoyé.
- Les administrateurs sont les comptes dont l'e-mail vérifié figure dans la configuration `Admin:Emails` (ADR 0020). En développement, la connexion de test accepte un e-mail pour créer un compte administrateur. Ils valident ou refusent les organisations, lisent et classent les signalements, suspendent ou rétablissent un compte.
- Suspendre un compte (ADR 0036) : arrête ses séries, annule ses balades à venir en prévenant les inscrits, supprime ses appareils et annule ses inscriptions à venir aux balades des autres. Ses liens avec ses chiens sont conservés : les autres propriétaires d'un chien partagé peuvent continuer à l'inscrire. Rétablir un compte remet seulement son statut à `Active` et ne restaure rien. Un compte suspendu n'est pas administrateur.

### 4.8 Suppression de compte

Un compte suspendu peut supprimer son compte : c'est la seule écriture qui lui reste permise (ADR 0036).

Supprimer son compte :

- annule ses balades à venir et prévient les inscrits ;
- supprime son lien avec chacun de ses chiens, et supprime un chien (avec ses inscriptions à venir annulées et sa photo effacée) seulement si l'utilisateur en était le dernier propriétaire ; un chien partagé reste à ses autres propriétaires ;
- supprime ses inscriptions, appareils, adhésions, blocages et jetons ;
- ferme (`Closed`) les organisations dont il était le seul `Owner` ;
- conserve ses messages et ses signalements, sans auteur, et les invitations de chien qu'il a créées ou acceptées, sans auteur (`createdById` et `usedById` nuls) ;
- conserve les balades qu'il a organisées, sans organisateur : `organizer` peut donc être nul dans les réponses (ADR 0023).

Après une suppression ou une suspension, le jeton d'accès encore valide cesse de fonctionner immédiatement : le compte est relu à chaque requête.

## 5. Points d'entrée

Tous sous `/api/v1`.

### Lot 1

| Méthode et chemin | Rôle |
| --- | --- |
| `POST /auth/apple` | Échange un jeton d'identité Apple contre une session ; crée le compte au premier passage |
| `POST /auth/google` | Idem avec un jeton d'identité Google |
| `POST /auth/dev` | Session pour un compte de test ; actif seulement si `Auth:AllowDevLogin` vaut vrai, et seulement en `Development` ou `Testing` |
| `POST /auth/refresh` | Renouvelle la session |
| `POST /auth/logout` | Révoque la famille du jeton de rafraîchissement |
| `GET /me`, `PATCH /me` | Lire et modifier son profil |
| `PUT /me/photo` | Envoyer sa photo (multipart) |
| `GET /me/dogs`, `POST /dogs` | Lister les chiens dont on est propriétaire, créer un chien (on en devient propriétaire) |
| `GET /dogs/{id}`, `PATCH /dogs/{id}`, `DELETE /dogs/{id}` | Lire un chien avec ses humains ; le modifier (tout propriétaire) ; le supprimer (dernier propriétaire seulement, sinon `dog.has_other_owners`) |
| `DELETE /dogs/{id}/members/me` | Ne plus s'occuper du chien ; `dog.last_owner` si l'on est le dernier propriétaire |
| `POST /dogs/{id}/invitations` | Créer une invitation (corps : `role`) ; renvoie le jeton, le lien `uzu://invitation/{jeton}` et `expiresAt`, une seule fois |
| `GET /dogs/{id}/invitations` | Invitations en attente du chien (propriétaires) : identifiant, rôle, auteur, `expiresAt`, sans jeton |
| `DELETE /dogs/{id}/invitations/{invitationId}` | Révoquer une invitation en attente |
| `GET /dog-invitations/{token}` | Consulter une invitation : chien, invitant, rôle, `expiresAt` ; `invitation.not_found`, `invitation.already_used` ou `invitation.expired` |
| `POST /dog-invitations/{token}/accept` | Accepter l'invitation connecté : crée le lien et renvoie le chien ; erreurs de §4.1 |
| `PUT /dogs/{id}/photo` | Envoyer la photo d'un chien |
| `GET /users/{id}` | Profil public et chiens dont l'utilisateur est propriétaire |
| `GET /health` | État du service |

### Lot 2

| Méthode et chemin | Rôle |
| --- | --- |
| `POST /walks` | Créer une balade |
| `GET /walks?lat&lng&radiusKm&from&to&size` | Rechercher les balades proches |
| `GET /walks/{id}` | Fiche, avec les participants, mes inscriptions et les inscriptions actives de mes chiens (`myDogsRegistrations`, §4.3) |
| `PATCH /walks/{id}`, `POST /walks/{id}/cancel` | Modifier, annuler |
| `GET /me/walks?role&when` | Mes balades : `role` vaut `Organizer` ou `Participant`, `when` vaut `Upcoming` ou `Past` |
| `POST /walks/{id}/registrations` | Inscrire un ou plusieurs chiens |
| `DELETE /walks/{id}/registrations/{registrationId}` | Annuler une inscription (son auteur ou un propriétaire du chien) |
| `POST /walks/{id}/registrations/{registrationId}/accept` et `/decline` | Décision de l'organisateur |

### Lot 3

| Méthode et chemin | Rôle |
| --- | --- |
| `GET /walks/{id}/messages?before&limit`, `POST /walks/{id}/messages` | Lire et écrire dans le fil |
| `PUT /me/devices`, `DELETE /me/devices/{token}` | Enregistrer ou retirer un appareil |
| `GET /me/notification-preferences`, `PUT /me/notification-preferences` | Préférences de notification |
| `POST /walks` avec `repeatWeekly` | Créer une balade hebdomadaire |
| `POST /walks/{id}/stop-series` | Arrêter la série |

### Lot 4

| Méthode et chemin | Rôle |
| --- | --- |
| `POST /organizations`, `GET /organizations?query`, `GET /organizations/{id}`, `PATCH /organizations/{id}` | Demander, chercher, lire, modifier une organisation |
| `GET /me/organizations` | Mes organisations et mon rôle |
| `POST /organizations/{id}/memberships` | Demander à rejoindre |
| `GET /organizations/{id}/memberships?limit` | Membres et demandes |
| `POST /organizations/{id}/memberships/{membershipId}/accept` et `/decline` | Décision sur une demande |
| `PATCH /organizations/{id}/memberships/{membershipId}` | Changer le rôle |
| `DELETE /organizations/{id}/memberships/{membershipId}` | Quitter ou retirer |
| `POST /blocks`, `GET /me/blocks`, `DELETE /blocks/{userId}` | Bloquer, lister, débloquer |
| `POST /reports` | Signaler |
| `GET /admin/organizations?status`, `POST /admin/organizations/{id}/approve` et `/reject` | Valider les organisations |
| `GET /admin/reports`, `PATCH /admin/reports/{id}` | Traiter les signalements |
| `POST /admin/users/{id}/suspend` et `/unsuspend` | Suspendre un compte |
| `DELETE /me` | Supprimer son compte |

### Développement seulement

Permis seulement en `Development` et `Testing`, anonymes (ADR 0027 et 0031).

| Méthode et chemin | Rôle |
| --- | --- |
| `POST /dev/demo-data` | Génère le jeu de données fictif autour de Seppois-le-Haut |
| `GET /dev/demo-data` | Dit si le jeu de démonstration est présent |
| `DELETE /dev/demo-data` | Supprime le jeu de démonstration |

Les listes (`/me/dogs`, `/walks`, `/me/walks`, `/organizations`, `/me/organizations`, `/me/blocks` et les listes d'administration) acceptent toutes `limit` (§1).

## 6. Architecture

### API (`api/`)

- .NET 10, minimal API, un dossier par fonctionnalité sous `src/Dogo.Api/Features/`.
- EF Core avec Npgsql et NetTopologySuite ; migrations versionnées.
- Tests d'intégration contre une vraie base PostgreSQL avec PostGIS, lancée par Testcontainers.
- `docker-compose.yml` pour la base locale, sur un port non standard.
- La description OpenAPI est générée à la compilation et versionnée dans `api/openapi.json`.
- Stockage des photos, envoi des notifications et envoi des e-mails derrière des interfaces, avec une implémentation locale ou de journalisation.

### Mobile (`mobile/`)

- Expo, TypeScript strict, navigation par fichiers.
- Client d'API et types générés depuis `api/openapi.json`.
- Session conservée dans le stockage sécurisé de l'appareil ; renouvellement automatique du jeton d'accès.
- Carte, notifications, appareil photo et connexion Apple/Google par les modules Expo ou compatibles.
- Un bouton de connexion de test apparaît seulement si `EXPO_PUBLIC_DEV_LOGIN` vaut `true`.
- Tous les textes affichés sont en français, regroupés dans un module.

## 7. Choix faits en autonomie

Ces points n'étaient pas tranchés dans le périmètre. Ils sont faciles à changer tant que le pilote n'a pas commencé.

| Sujet | Choix | Pourquoi |
| --- | --- | --- |
| Catégories de gabarit | Petit, Moyen, Grand | Le périmètre demande au minimum petit et grand |
| Échelles de sociabilité et de réactivité | Trois niveaux chacune | Assez pour filtrer, sans prétendre à un diagnostic |
| Choix du lieu | L'organisateur pose un point sur la carte et écrit un libellé | Évite de dépendre d'un service de recherche d'adresses |
| Rappel | 2 heures avant le début | Assez tôt pour s'organiser, assez tard pour être utile |
| Connexion de test | Point d'entrée `/auth/dev`, désactivé par défaut | Permet de tester sans comptes Apple ni Google |
| Photos | Fichiers sur le disque du serveur | L'hébergement n'est pas choisi |
| Alerte de signalement | Notification push aux administrateurs, sans e-mail (ADR 0036) | Aucun fournisseur d'e-mails n'est choisi |
| Administration | Points d'entrée `/admin`, sans écran | Suffisant à l'échelle d'un pilote |
| Nom de l'application | Uzu (nom affiché), slug Expo `uzu` | Décision du porteur du projet (ADR 0041) ; le dépôt et le code gardent le nom `dogo` |
| Identifiant de l'application | `com.victorsauter.uzu` (bundle iOS et paquet Android), schéma de lien `uzu://` | Figé au premier envoi à un store (ADR 0039, 0041) |
| Invitation de chien | Lien `uzu://invitation/{jeton}`, jeton gardé haché, à usage unique, 7 jours, révocable, valable même si son auteur quitte le chien | Décidé par le porteur du projet (ADR 0042) ; forme du lien, hachage et survie à l'auteur choisis par l'agent |
| Départ d'un propriétaire | Ses inscriptions déjà faites pour le chien restent | Évite d'annuler en silence ce qu'un humain a organisé ; à confirmer |
| Inscriptions des chiens partagés sur la fiche | Liste `myDogsRegistrations` à côté de `myRegistrations`, sans changer cette dernière | Le client affiche « Déjà inscrit par {prénom} » sans recouper les participants ; `myRegistrations` garde son sens (choix de l'agent API) |
| Fuseau des séries | `Europe/Paris` | Le pilote est en France |
| Centre de la carte par défaut | Seppois-le-Haut, réglable par configuration | C'est la zone pilote |
| Données de démonstration | Jeu fictif autour de Seppois-le-Haut, activable en développement seulement (ADR 0027) | Essayer l'application sans attendre de vrais utilisateurs |

L'hébergement (serveur, base, sauvegardes) n'est pas choisi : voir le [guide de déploiement](deploiement.md).
