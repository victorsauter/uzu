# Uzu — Points ouverts à trancher

**Date :** 23/11/2026 · document de travail hors ligne, tiré des documents du dépôt (rien n'est inventé ; ce qui est une déduction est marqué « (déduit) »).

## Comment utiliser ce document

- Chaque point est une case à cocher : `- [ ]` devient `- [x]` une fois tranché.
- Écrire la décision sur place, après `Décision :`. Une phrase suffit.
- Les sections vont de la plus bloquante (publication) à la moins urgente. Les points qui dépendent de la même chose sont regroupés ; les sources sont citées sous chaque point.
- Au retour : reporter les décisions dans le périmètre, la spécification ou un ADR (un ADR « Proposé » validé passe à « Accepté », voir [la liste des ADR](adr/README.md)).

## Vue d'ensemble

| Section | Points | Priorité |
| --- | --- | --- |
| 1. Décisions produit à trancher avant publication | 17 | Haute : bloque les stores et le pilote |
| 2. ADR « Proposé » à valider | 14 | Moyenne : aucune urgence technique, mais ce sont des décisions non validées |
| 3. Choix faits en autonomie dans la spécification | 14 | Moyenne : faciles à changer tant que le pilote n'a pas commencé |
| 4. Exclusions du périmètre V1 et candidats V2 | 14 | Basse : confirmer que cela reste dehors |
| 5. Reste à faire technique | 9 | Moyenne : essais sur appareil, Android, Balades |
| 6. Infrastructure et mise en ligne | 7 | Haute pour l'hébergeur de production, basse pour le reste |
| **Total** | **75** | |

---

## 🔴 1. Décisions produit à trancher avant publication

Ce que le plan appelle « ce qui t'appartient » : comptes, contenus, règles. Tant que ce n'est pas fait, l'application ne peut ni être publiée ni ouverte à des testeurs.

### Contenu légal et vie privée

- [ ] **Textes légaux à fournir** — Quel éditeur, quelle adresse, quel contact, quel hébergeur et quelle durée de conservation mettre dans les conditions d'utilisation et la politique de confidentialité ?
  - Contexte : ces champs sont des marqueurs `[…]` affichés tels quels dans l'application, sous un bandeau « Version provisoire — à faire valider avant la publication ». C'est du contenu, pas du code.
  - Sources : [revue (MOB-3)](revue-v1.md), [plan](plan.md), [ADR 0025](adr/0025-pages-legales-hors-garde.md), [déploiement](deploiement.md).
  - Décision :

- [ ] **Cadrage juridique par un juriste** — Faire cadrer par un juriste, avant le lancement, la responsabilité de la plateforme, les données personnelles et les conditions d'utilisation ? Qui, quand ?
  - Contexte : trois sujets listés dans le périmètre : (1) responsabilité si un chien déjà signalé blesse ensuite ; (2) signalements nominatifs : durée de conservation et réponse à une demande d'accès de la personne visée ; (3) règles françaises sur les chiens de 1re et 2e catégorie.
  - Sources : [périmètre](perimetre-v1.md) (section Sécurité et signalements), [plan](plan.md) (ligne Juridique), [revue](revue-v1.md).
  - Décision :

- [ ] **Chiens de 1re et 2e catégorie** — Ajouter un champ ou une règle (laisse, muselière dans les lieux publics) aux balades et aux chiens, ou reformuler la promesse du périmètre ?
  - Contexte : le périmètre dit de les reprendre dans les règles des balades ; la revue constate qu'il n'existe ni règle ni champ.
  - Sources : [périmètre](perimetre-v1.md), [revue](revue-v1.md) (section Documentation).
  - Décision :

- [ ] **Déclarations de données personnelles** — Quelle déclaration RGPD et quelles déclarations de confidentialité des stores (fiche Google Play « sécurité des données », étiquettes de l'App Store) ? Avec quels sous-traitants ?
  - Contexte : le guide de déploiement dit que la déclaration RGPD, l'hébergeur et les sous-traitants sont « à confirmer » et bloquants pour des testeurs ; le README mobile liste la politique de confidentialité et la sécurité des données parmi les tâches de la fiche Play Console.
  - Sources : [déploiement](deploiement.md) (section 6), [README mobile](../mobile/README.md), [plan](plan.md).
  - Décision :

- [ ] **Révocation du jeton Apple à la suppression du compte** — Que faut-il faire pour satisfaire les exigences actuelles d'Apple ?
  - Contexte : la revue note que ces exigences n'ont pas été vérifiées ; la suppression de compte est exigée par l'App Store (déduit : à traiter avant la première soumission iOS).
  - Sources : [revue](revue-v1.md) (« Ce qui n'a pas pu être vérifié »), [plan](plan.md) (Risques).
  - Décision :

### Modération et signalements

- [ ] **Canal de contact et droit de réponse** — Quelle adresse e-mail de contact, et quelle procédure pour le droit de réponse d'une personne signalée ?
  - Contexte : le périmètre dit que le droit de réponse passe par e-mail ; la revue constate qu'aucun canal ni contact n'est défini.
  - Sources : [périmètre](perimetre-v1.md), [revue](revue-v1.md).
  - Décision :

- [ ] **Alerte des signalements** — La notification push aux administrateurs suffit-elle pour la V1, ou faut-il un envoi d'e-mails (donc un fournisseur d'e-mails) ?
  - Contexte : le périmètre promet qu'un signalement « arrive chez toi » ; en pratique il est journalisé et notifié par push (catégorie seulement), et lisible par `GET /admin/reports`. `Admin:ReportEmail` existe mais n'est pas utilisée ; aucun fournisseur d'e-mails n'est choisi.
  - Sources : [revue (SEC-10)](revue-v1.md), [ADR 0036](adr/0036-suspension-et-signalements.md), [spécification §7](specification-v1.md#7-choix-faits-en-autonomie), [déploiement](deploiement.md), [README API](../api/README.md).
  - Décision :

- [ ] **Administration sans écran** — Garder l'administration par points d'entrée `/admin` (validation des organisations, lecture des signalements, suspension) pour le pilote, ou construire un écran avant ?
  - Contexte : les administrateurs sont désignés par leur e-mail (`Admin__Emails`) ; le guide de déploiement dit qu'un meilleur outil d'administration est à prévoir si le pilote grandit ; la modération outillée est hors V1 (« quand le volume l'exige »).
  - Sources : [ADR 0020](adr/0020-administration-par-e-mails-configures.md), [déploiement](deploiement.md), [périmètre](perimetre-v1.md), [spécification §7](specification-v1.md#7-choix-faits-en-autonomie).
  - Décision :

- [ ] **Compte suspendu qui supprime son compte (RF-1)** — Autoriser un compte suspendu à se connecter pour cette seule action ?
  - Contexte : l'API accepte la suppression, mais un compte suspendu ne peut plus s'authentifier : elle n'est possible que pendant les 15 minutes de sa session ouverte. Recommandation de la revue : l'autoriser pour cette seule action.
  - Sources : [revue](revue-v1.md) (Décisions à prendre, Ce qui reste ouvert), [ADR 0036](adr/0036-suspension-et-signalements.md).
  - Décision :

- [ ] **Phrases du périmètre qui ne sont plus vraies** — Reformuler ces promesses (signalement « chez toi », droit de réponse, chiens de 1re et 2e catégorie, feuille de route) ?
  - Contexte : recommandation de la revue : les reformuler. Se règle en même temps que les trois points précédents.
  - Sources : [revue](revue-v1.md), [périmètre](perimetre-v1.md).
  - Décision :

### Comptes et publication

- [ ] **Compte Apple Developer et connexion Apple** — Créer le compte (99 USD par an) et configurer la connexion Apple ?
  - Contexte : indispensable pour TestFlight, la connexion Apple dans une build et les notifications iOS. Tant qu'il n'existe pas, la connexion de test remplace la connexion Apple, et la build de démo à Apple ID gratuit n'a ni connexion Apple ni notifications.
  - Sources : [plan](plan.md) (Ce que ce plan ne couvre pas), [README mobile](../mobile/README.md), [ADR 0006](adr/0006-connexion-apple-google.md), [ADR 0043](adr/0043-demo-hebergee.md).
  - Décision :

- [ ] **Connexion Google** — Créer le projet Google Cloud et les identifiants OAuth (iOS, Android) ?
  - Contexte : sans identifiant Google, Android n'a aucun moyen de se connecter ; la connexion Google n'a jamais été essayée sur appareil réel.
  - Sources : [plan](plan.md), [revue (MOB-4)](revue-v1.md), [README mobile](../mobile/README.md).
  - Décision :

- [ ] **Compte et projet Expo (EAS), notifications réelles** — Créer le compte Expo et le projet EAS, et un projet Firebase (FCM) pour les notifications Android ?
  - Contexte : sans projet EAS, les notifications sont journalisées côté serveur, pas reçues. EAS gratuit : environ 15 builds par plateforme (quota « à vérifier »), ensuite à partir de 19 USD par mois.
  - Sources : [plan](plan.md), [README mobile](../mobile/README.md), [ADR 0022](adr/0022-notifications-push-mobile.md), [ADR 0039](adr/0039-profils-de-build-eas.md).
  - Décision :

- [ ] **Distribuer Android au pilote ?** — Oui ou non, et si oui quand lancer le test fermé ?
  - Contexte : Android demande une clé Google Maps (compte de facturation avec carte bancaire), des identifiants Google, un compte Play Console (25 USD, vérification d'identité) et, pour un compte personnel récent, un test fermé de 12 testeurs pendant 14 jours avant la production. Android n'a jamais été validé sur appareil réel. Le plan de l'onglet Balades dit que la durée de 14 jours est incompressible et qu'il faut ouvrir le test le plus tôt possible.
  - Options : iPhone seulement au pilote / iPhone et Android.
  - Sources : [revue (MOB-4)](revue-v1.md), [README mobile](../mobile/README.md), [plan](plan.md) (Risques), [plan Balades](plan-panneau-balades.md).
  - Décision :

- [ ] **Fiches des stores** — Qui rédige les textes, captures et réponses de classification des deux fiches, et quand soumettre ?
  - Contexte : la publication demande comptes, fiches et validation ; l'identifiant `com.victorsauter.uzu` est figé au premier envoi à un store.
  - Sources : [plan](plan.md), [spécification §7](specification-v1.md#7-choix-faits-en-autonomie), [README mobile](../mobile/README.md).
  - Décision :

### Pilote

- [ ] **Critère de réussite du pilote** — Quel seuil, une fois le nombre d'éducateurs partenaires connu ?
  - Contexte : seule décision ouverte du périmètre. Point de départ proposé : 10 balades par semaine pendant 4 semaines, dont la moitié créées par des particuliers. Elle bloque la décision de passer à la suite.
  - Sources : [périmètre](perimetre-v1.md).
  - Décision :

- [ ] **Éducateurs partenaires et paiement** — Que faire si un éducateur refuse sans paiement intégré : faire passer le lot pros 2 avant la V1.1 ?
  - Contexte : c'est le risque principal d'amorçage identifié ; la V1 recrute 1 à 3 éducateurs ou clubs avant les particuliers. Le périmètre dit que le lot pros 2 passe avant la V1.1 si les éducateurs en font une condition.
  - Sources : [périmètre](perimetre-v1.md) (Amorçage, Feuille de route).
  - Décision :

---

## 🟡 2. ADR « Proposé » à valider

Douze ADR sont au statut « Proposé » : recommandations en attente de validation. Pour chacun, la question est la même : **valider** (le statut passe à « Accepté ») ou **modifier** (un nouvel ADR le remplace). Un ADR accepté ne se réécrit pas.

- [ ] **[ADR 0031](adr/0031-durcissement-de-l-api.md) — Durcissement de l'API** — Valider ou modifier ?
  - Décide : environnements en liste blanche (outils de développement seulement en `Development` et `Testing`, tout autre nom traité comme la production), limitation de débit (429) en trois fenêtres, en-têtes transférés lus seulement derrière un proxy configuré, HSTS, `nosniff`, corps limité à 256 Ko. Précisé par l'ADR 0043 (environnement `Demo`).
  - Source : [ADR 0031](adr/0031-durcissement-de-l-api.md), [revue](revue-v1.md).
  - Décision :

- [ ] **[ADR 0032](adr/0032-nettoyage-des-photos.md) — Nettoyage des photos** — Valider ou modifier ?
  - Décide : décodage puis réécriture côté serveur (SkiaSharp) sans aucune métadonnée (GPS compris), HEIC refusé, 25 mégapixels au plus, réduction à 2 048 px, cache privé d'un jour. Précise l'ADR 0010.
  - Source : [ADR 0032](adr/0032-nettoyage-des-photos.md).
  - Décision :

- [ ] **[ADR 0033](adr/0033-fin-de-session-et-delais-reseau.md) — Fin de session et délais réseau (mobile)** — Valider ou modifier ?
  - Décide : seuls un 401 ou un 403 `account.suspended` sur le renouvellement ferment la session ; fin de session expliquée à l'écran ; délai de 20 s par requête ; écran d'erreur de configuration hors développement ; persistance sûre de la session. Décidé par l'agent mobile, sur délégation. Précise les ADR 0014 et 0022.
  - Source : [ADR 0033](adr/0033-fin-de-session-et-delais-reseau.md).
  - Décision :

- [ ] **[ADR 0034](adr/0034-robustesse-des-ecrans.md) — Robustesse des écrans** — Valider ou modifier ?
  - Décide : fil de messages en cache cumulatif, écritures qui rendent la main sans attendre le rechargement, navigation seulement si l'écran a encore le focus, repère de balade placé volontairement, préférences de notification enregistrées l'une après l'autre. Décidé par l'agent mobile, sur délégation.
  - Source : [ADR 0034](adr/0034-robustesse-des-ecrans.md).
  - Décision :

- [ ] **[ADR 0035](adr/0035-rotation-des-jetons-delai-de-grace.md) — Rotation des jetons de rafraîchissement** — Valider ou modifier ?
  - Décide : familles de jetons, délai de grâce (60 s par défaut) qui donne une nouvelle session au lieu d'une alerte, révocation par famille (pas tous les appareils), purge à 30 jours. Précise l'ADR 0009.
  - Source : [ADR 0035](adr/0035-rotation-des-jetons-delai-de-grace.md).
  - Décision :

- [ ] **[ADR 0036](adr/0036-suspension-et-signalements.md) — Suspension et signalements** — Valider ou modifier ?
  - Décide : suspendre un compte = « geler, annuler, laisser partir » (séries arrêtées, balades à venir annulées, appareils retirés, inscriptions aux balades d'autrui annulées) ; un compte suspendu peut supprimer son compte ; signalement notifié par push aux administrateurs. Décisions du porteur, sauf l'annulation des inscriptions aux balades d'autrui (ajout non demandé, voir section 3). Précise les ADR 0020 et 0023.
  - Source : [ADR 0036](adr/0036-suspension-et-signalements.md), [revue](revue-v1.md).
  - Décision :

- [ ] **[ADR 0037](adr/0037-organisations-gelees-et-blocage-indiscernable.md) — Organisations gelées, blocage indiscernable, concurrence** — Valider ou modifier ?
  - Décide : une organisation non validée gèle ses balades sans les annuler ; une personne bloquée reçoit la réponse exacte d'une balade complète (`walk.full`) ; écritures concurrentes par conditions et verrous. Les deux premières règles sont des décisions du porteur ; le mécanisme anti-course est un choix de l'agent API. Précise l'ADR 0015.
  - Source : [ADR 0037](adr/0037-organisations-gelees-et-blocage-indiscernable.md).
  - Décision :

- [ ] **[ADR 0038](adr/0038-image-et-migrations-en-production.md) — Image de l'API et migrations en production** — Valider ou modifier ?
  - Décide : image .NET Ubuntu non root (uid 1654), volume `/data/photos`, bundle de migrations EF Core lancé à chaque déploiement ; une seule instance en écriture. Précisé par l'ADR 0043 (migrations au démarrage en `Demo`).
  - Source : [ADR 0038](adr/0038-image-et-migrations-en-production.md), [déploiement](deploiement.md).
  - Décision :

- [ ] **[ADR 0039](adr/0039-profils-de-build-eas.md) — Profils de build EAS** — Valider ou modifier ?
  - Décide : quatre profils (`development`, `development-simulator`, `preview`, `production`), variables d'environnement EAS (jamais dans le dépôt), numéros de build distants, TestFlight par `preview`, garde-fou qui refuse une build de distribution sans API en `https` ou avec la connexion de test. Décidé par l'agent mobile, sur délégation, d'après la documentation d'Expo du jour.
  - Source : [ADR 0039](adr/0039-profils-de-build-eas.md), [README mobile](../mobile/README.md).
  - Décision :

- [ ] **[ADR 0040](adr/0040-navigation-gardee-et-rafraichissement-apres-refus.md) — Navigation gardée et rafraîchissement après refus** — Valider ou modifier ?
  - Décide : un garde contre les appuis répétés, rafraîchissement des données après un refus 404 ou 409, « Retour » plutôt que « Réessayer » sur une erreur définitive, mise en page qui suit la taille du texte. Décidé par l'agent mobile, sur délégation ; limite connue : un appui qui tombe sur un nouvel écran agit sur lui.
  - Source : [ADR 0040](adr/0040-navigation-gardee-et-rafraichissement-apres-refus.md).
  - Décision :

- [ ] **[ADR 0042](adr/0042-chien-relie-a-plusieurs-humains.md) — Un chien relié à plusieurs humains** — Valider ou modifier ?
  - Décide : liens `DogMember` (rôle `Owner` seul en V1.x), droits égaux entre propriétaires, invitation par lien `uzu://invitation/{jeton}` (usage unique, 7 jours, révocable), plafond de 20 chiens par humain. Décisions du porteur, « à confirmer à l'implémentation » ; quatre questions restent ouvertes dans l'ADR (voir section 4). Précise les ADR 0023 et 0037.
  - Source : [ADR 0042](adr/0042-chien-relie-a-plusieurs-humains.md), [spécification §4.1](specification-v1.md).
  - Décision :

- [ ] **[ADR 0043](adr/0043-demo-hebergee.md) — Démo hébergée gratuitement** — Valider ou modifier ?
  - Décide : environnement `Demo` (connexion de test et données de démonstration ouvertes), photos en stockage S3, API sur Render, base et stockage sur Supabase, appel périodique de santé par cron-job.org, build iPhone avec Apple ID gratuit. Ne décide rien pour la production. Précise les ADR 0010, 0027, 0031 et 0038.
  - Source : [ADR 0043](adr/0043-demo-hebergee.md), [démo hébergée](demo-hebergee.md).
  - Décision :

- [ ] **ADR « acceptés sur délégation, à confirmer » (20)** — Les confirmer en bloc, ou en reprendre certains ?
  - Contexte : statut « Accepté » dans le fichier, mais avec une note « accepté par l'orchestrateur (ou l'agent), sur délégation, pour lancer le développement ; à confirmer ». Numéros : 0005, 0007, 0008, 0009, 0010, 0011, 0012, 0013, 0014, 0015, 0017, 0018, 0019, 0020, 0021, 0022, 0023, 0025, 0026, 0027. La revue en comptait 18 ; ce décompte est celui des fichiers actuels.
  - Sources : [liste des ADR](adr/README.md), [revue](revue-v1.md) (Décisions à prendre).
  - Décision :

- [ ] **Statuts de 0033 et 0034** — Corriger la phrase du README des ADR ?
  - Contexte : elle dit que les ADR 0033 et 0034 sont « déjà Accepté dans leur fichier (à confirmer) », mais leurs fichiers et le tableau disent « Proposé » (déduit : phrase périmée).
  - Source : [liste des ADR](adr/README.md).
  - Décision :

---

## 🟡 3. Choix faits en autonomie dans la spécification

La spécification (§7) liste les choix pris sans validation explicite ; ils sont faciles à changer tant que le pilote n'a pas commencé. Question commune : **garder** ou **changer** ? Les points « alerte de signalement » et « administration par `/admin` » de ce même tableau sont déjà en section 1.

- [ ] **Catégories de gabarit** — Garder Petit, Moyen, Grand ?
  - Contexte : le périmètre demande au minimum petit et grand ; trois catégories ont été retenues.
  - Source : [spécification §7](specification-v1.md#7-choix-faits-en-autonomie).
  - Décision :

- [ ] **Échelles de sociabilité et de réactivité** — Garder trois niveaux chacune ?
  - Contexte : assez pour filtrer, sans prétendre à un diagnostic.
  - Source : [spécification §7](specification-v1.md#7-choix-faits-en-autonomie).
  - Décision :

- [ ] **Choix du lieu de rendez-vous** — Garder « un point posé sur la carte et un libellé écrit par l'organisateur », sans recherche d'adresses ?
  - Contexte : évite de dépendre d'un service de recherche d'adresses. Le périmètre veut le point exact visible par tous.
  - Source : [spécification §7](specification-v1.md#7-choix-faits-en-autonomie), [périmètre](perimetre-v1.md).
  - Décision :

- [ ] **Délai du rappel** — Garder 2 heures avant le début ?
  - Contexte : assez tôt pour s'organiser, assez tard pour être utile.
  - Source : [spécification §7](specification-v1.md#7-choix-faits-en-autonomie).
  - Décision :

- [ ] **Fuseau des séries et affichage des heures** — Garder `Europe/Paris` pour les séries hebdomadaires, et l'affichage de l'heure d'une balade dans le fuseau de celui qui la regarde ?
  - Contexte : le pilote est en France. Le README mobile marque l'affichage dans le fuseau de l'appareil (pas celui du lieu) comme « choix de produit à confirmer ».
  - Sources : [spécification §7](specification-v1.md#7-choix-faits-en-autonomie), [README mobile](../mobile/README.md).
  - Décision :

- [ ] **Centre de carte par défaut** — Garder Seppois-le-Haut, réglable par configuration ?
  - Source : [spécification §7](specification-v1.md#7-choix-faits-en-autonomie).
  - Décision :

- [ ] **Connexion de test et données de démonstration** — Garder le point d'entrée `/auth/dev` (désactivé par défaut) et le jeu fictif autour de Seppois-le-Haut, activables hors production seulement ?
  - Contexte : permet de tester sans comptes Apple ni Google. Les photos des comptes générés viennent de DiceBear et de Wikimedia Commons, certaines sous licence CC BY ou CC BY-SA : elles ne se publient pas.
  - Sources : [spécification §7](specification-v1.md#7-choix-faits-en-autonomie), [ADR 0027](adr/0027-donnees-de-demonstration.md), [démo hébergée](demo-hebergee.md).
  - Décision :

- [ ] **Photos sur le disque du serveur** — Garder le stockage local en production, ou passer au stockage S3 déjà écrit pour la démo ?
  - Contexte : choisi parce que l'hébergement n'était pas choisi ; le disque impose une seule instance. Voir aussi la section 6.
  - Sources : [spécification §7](specification-v1.md#7-choix-faits-en-autonomie), [ADR 0010](adr/0010-stockage-des-photos.md), [ADR 0043](adr/0043-demo-hebergee.md).
  - Décision :

- [ ] **Identifiants de l'application** — Confirmer le nom Uzu, `com.victorsauter.uzu` et le schéma `uzu://` avant le premier envoi à un store ?
  - Contexte : décidés par le porteur (ADR 0041) mais figés au premier envoi à un store (ADR 0039) ; changer ensuite n'est plus possible simplement (déduit).
  - Sources : [spécification §7](specification-v1.md#7-choix-faits-en-autonomie), [ADR 0041](adr/0041-nom-de-l-application-uzu.md), [ADR 0039](adr/0039-profils-de-build-eas.md).
  - Décision :

- [ ] **Forme de l'invitation de chien** — Valider le lien `uzu://invitation/{jeton}`, le jeton gardé haché, et la validité de l'invitation après le départ de son auteur ?
  - Contexte : l'invitation elle-même est une décision du porteur ; la forme du lien, le hachage et la survie à l'auteur sont des choix de l'agent.
  - Sources : [spécification §7](specification-v1.md#7-choix-faits-en-autonomie), [ADR 0042](adr/0042-chien-relie-a-plusieurs-humains.md).
  - Décision :

- [ ] **Départ d'un propriétaire** — Ses inscriptions déjà faites pour le chien restent-elles (choix actuel), ou sont-elles annulées ?
  - Contexte : évite d'annuler en silence ce qu'un humain a organisé ; marqué « à confirmer ». L'ADR 0042 dit « à revoir si cela surprend à l'usage ».
  - Sources : [spécification §7](specification-v1.md#7-choix-faits-en-autonomie), [ADR 0042](adr/0042-chien-relie-a-plusieurs-humains.md).
  - Décision :

- [ ] **Liste `myDogsRegistrations` sur la fiche de balade** — Garder une liste à côté de `myRegistrations` plutôt que changer le sens de celle-ci ?
  - Contexte : permet d'afficher « Déjà inscrit par {prénom} » ; choix de l'agent API.
  - Source : [spécification §7](specification-v1.md#7-choix-faits-en-autonomie).
  - Décision :

- [ ] **Balade à un an au plus** — Garder la règle « une balade commence au plus un an après sa création ou sa modification » ?
  - Contexte : règle ajoutée par la revue, « à confirmer par le porteur du projet ».
  - Sources : [spécification §4.2](specification-v1.md), [revue](revue-v1.md).
  - Décision :

- [ ] **Ajouts faits sans demande explicite, retirables (6)** — Les garder tous (recommandation de la revue) ou en retirer ?
  - Contexte : (1) la suspension annule aussi les inscriptions du compte aux balades des autres ; (2) début de balade à un an au plus ; (3) l'épingle du rendez-vous doit être placée volontairement même quand la position est connue ; (4) le départ d'un membre annule ses inscriptions sans prévenir l'organisateur ; (5) dépendances mobiles ajoutées : `expo-dev-client`, `expo-system-ui` ; (6) icône de notification provisoire (patte blanche).
  - Source : [revue](revue-v1.md) (Corrections, Ajouts faits sans demande explicite).
  - Décision :

---

## 🔵 4. Exclusions du périmètre V1 et candidats V2

Rien n'est abandonné : chaque sujet est rangé dans une étape ultérieure. Question commune : **cela reste-t-il dehors** à la date prévue, ou doit-il entrer plus tôt ?

- [ ] **Mini-évaluation après-balade et badges communautaires (V1.1)** — Rester en V1.1 ?
  - Contexte : demande du volume et un seuil minimal de réponses pour préserver l'anonymat ; la v0.2 les plaçait à la fois en MVP et en V1.1, tranché en V1.1.
  - Source : [périmètre](perimetre-v1.md).
  - Décision :

- [ ] **Favoris et affinités entre chiens (V1.1)** — Rester en V1.1 ?
  - Contexte : utile seulement quand les gens se sont déjà croisés.
  - Source : [périmètre](perimetre-v1.md).
  - Décision :

- [ ] **Lot pros 2 : paiement, catalogue, agenda, tableau de bord** — Rester après la V1, avec le paiement hors application ?
  - Contexte : prestataire de paiement, remboursements, annulations et commission non définis ; catalogue, agenda et réservation sont « un produit en soi » ; le tableau de bord dépend du paiement. Passe avant la V1.1 si les éducateurs l'exigent (voir section 1).
  - Source : [périmètre](perimetre-v1.md).
  - Décision :

- [ ] **Certification des éducateurs et badges éducateurs (plus tard)** — Rester dehors ?
  - Contexte : processus de vérification non défini.
  - Source : [périmètre](perimetre-v1.md).
  - Décision :

- [ ] **Modération outillée** — Rester à la main (restrictions graduées, contestation dans l'application hors V1) ?
  - Contexte : traitable à la main à l'échelle d'une zone pilote ; « quand le volume l'exige ». À lier au point « administration sans écran » de la section 1.
  - Source : [périmètre](perimetre-v1.md).
  - Décision :

- [ ] **Accompagnement des chiens en difficulté, IA d'assistance (plus tard)** — Rester dehors ?
  - Contexte : suppose des éducateurs certifiés et un historique d'incidents.
  - Source : [périmètre](perimetre-v1.md).
  - Décision :

- [ ] **Santé, garde, pensions, activités sportives (plus tard)** — Rester dehors ?
  - Contexte : hors de la promesse initiale.
  - Source : [périmètre](perimetre-v1.md).
  - Décision :

- [ ] **Application équine** — Toujours mise de côté jusqu'à ce que la V1 canine atteigne son critère de réussite ?
  - Contexte : comparateur d'écuries, autres utilisateurs et parcours ; aucun paquet commun tant qu'il n'y a qu'un produit. Idée à conserver : distinguer le déclaré par l'écurie du confirmé par les clients.
  - Source : [périmètre](perimetre-v1.md).
  - Décision :

- [ ] **Rôles `Trusted` et `Educator` sur un chien (phase 2)** — Quels droits, qui les attribue, quel affichage, quel effet sur l'inscription et le plafond ?
  - Contexte : prévus mais non acceptés par l'API ; question ouverte de l'ADR 0042.
  - Source : [ADR 0042](adr/0042-chien-relie-a-plusieurs-humains.md#questions-ouvertes), [spécification §4.1](specification-v1.md).
  - Décision :

- [ ] **Retirer un autre propriétaire d'un chien** — Le permettre, et avec quelle protection contre un retrait abusif ?
  - Contexte : aujourd'hui seul un propriétaire peut se retirer lui-même ; cas évoqués : séparation, invitation à la mauvaise personne.
  - Source : [ADR 0042](adr/0042-chien-relie-a-plusieurs-humains.md#questions-ouvertes).
  - Décision :

- [ ] **Plafond d'invitations en attente par chien** — Aucun pour l'instant ; en ajouter un seulement si un abus apparaît ?
  - Contexte : la limitation de débit de l'ADR 0031 s'applique déjà.
  - Source : [ADR 0042](adr/0042-chien-relie-a-plusieurs-humains.md#questions-ouvertes).
  - Décision :

- [ ] **Pas de temps réel** — Confirmer que les changements d'autrui n'apparaissent qu'au retour sur l'écran, en tirant pour rafraîchir, ou toutes les 15 s dans le fil ?
  - Contexte : « fonctionnement prévu en V1 » ; notifications push sans temps réel.
  - Sources : [plan](plan.md), [ADR 0008](adr/0008-notifications-sans-temps-reel.md).
  - Décision :

- [ ] **Écran d'administration et lien de la notification de signalement** — Hors V1, à construire plus tard ?
  - Contexte : la notification `report_created` n'a pas de lien ; l'ADR 0036 dit qu'un écran d'administration pourra plus tard lui en donner un.
  - Source : [ADR 0036](adr/0036-suspension-et-signalements.md).
  - Décision :

- [ ] **Mises à jour sans passer par les stores (`expo-updates`)** — Les ajouter plus tard ?
  - Contexte : non installé ; le canal d'EAS est prêt pour plus tard. Le périmètre rappelle qu'Expo permet de corriger le code applicatif sans repasser par la validation des stores (déduit : à décider avant la publication).
  - Sources : [README mobile](../mobile/README.md), [périmètre](perimetre-v1.md).
  - Décision :

---

## 🔧 5. Reste à faire technique

Court : ce qui reste après la V1 développée et les corrections de la revue. Certains points contiennent une décision produit.

### Onglet Balades (panneau et liste)

- [ ] **Essai des deux vues avec des utilisateurs, puis règle définitive** — Quand faire l'essai, avec qui, et garder une seule vue ou les deux ?
  - Contexte : tâche 9, « hors code », non faite. Trois consignes sont écrites (trouver la balade la plus proche, montrer les balades de la semaine, ouvrir une balade et dire combien de chiens sont inscrits), avec des personnes peu à l'aise avec un téléphone. Le résultat s'écrit dans un nouvel ADR (une seule vue, ou les deux avec leur vue par défaut). Le choix reste ouvert tant que l'essai n'a pas eu lieu.
  - Sources : [plan Balades](plan-panneau-balades.md), [ADR 0028](adr/0028-balades-carte-et-panneau-coulissant.md).
  - Décision :

- [ ] **Questions ouvertes de l'onglet Balades** — Quelles pistes retenir ?
  - Contexte : (1) position du panneau à l'ouverture sans balade proche (piste : basse) ; (2) moyen d'actualiser (piste : automatique au retour sur l'onglet, plus un bouton discret) ; (3) choisir la vue depuis l'onglet lui-même (piste : plus tard) ; (4) vue par défaut selon l'appareil (à décider après l'essai).
  - Source : [plan Balades](plan-panneau-balades.md) (Questions ouvertes).
  - Décision :

- [ ] **Android, lecteur d'écran, petit écran** — Planifier ces vérifications ?
  - Contexte : tâche 7 (Android : émulateur et appareil réel, gestes du panneau, carte Google) non faite ; tâche 8 (lecteur d'écran) partielle, libellés posés mais jamais écoutés ; tâche 6 (petit écran, texte agrandi) partielle, une recette a relevé des textes tronqués en très grande police.
  - Sources : [plan Balades](plan-panneau-balades.md), [ADR 0029](adr/0029-menus-ancres-et-finition-native.md).
  - Décision :

- [ ] **Nettoyage de l'onglet Balades** — Tests, README mobile, suppression de la branche du prototype, retrait de la vue non retenue (tâche 10) ?
  - Source : [plan Balades](plan-panneau-balades.md).
  - Décision :

### Jamais essayé, et défauts connus

- [ ] **Essais sur appareil réel** — Qui les fait, avec quels comptes ?
  - Contexte : jamais essayés faute de comptes ou d'appareil : connexion Apple et Google, réception d'une notification, appareil photo, application sur un appareil physique, création automatique de la balade suivante d'une série dans la durée. Dépend des comptes de la section 1.
  - Sources : [plan](plan.md), [revue](revue-v1.md), [déploiement](deploiement.md) (section 6).
  - Décision :

- [ ] **Blocage : la fiche montre les places prises** — Masquer ou accepter que la personne bloquée voie des places libres sur une balade qui répond « complète » ?
  - Contexte : défaut connu non corrigé ; la réponse d'inscription est indiscernable (ADR 0037) mais pas la fiche.
  - Sources : [revue](revue-v1.md) (Ce qui reste ouvert), [ADR 0037](adr/0037-organisations-gelees-et-blocage-indiscernable.md).
  - Décision :

- [ ] **Défauts connus non corrigés** — Les corriger avant le pilote ou les accepter ?
  - Contexte : `HEAD /photos/…` répond 401 (REC-5, non traité) ; suppression de compte et inscription simultanées depuis le même compte peuvent répondre 500 (très improbable, sans effet sur les données) ; Android à 200 % de police : icônes « + » et carte qui débordent ; un second appui peut agir sur le nouvel écran (ADR 0040) ; dix volumes Docker anonymes vides à supprimer à la main.
  - Source : [revue](revue-v1.md) (Ce qui reste ouvert après corrections).
  - Décision :

- [ ] **Constats mineurs de la revue** — Lesquels restent à traiter ?
  - Contexte : la revue en liste une trentaine (API et mobile) à corriger « quand on y passe » ; les lots 2 à 3e en ont corrigé une partie (déduit : aucun suivi ligne à ligne n'existe dans les documents).
  - Source : [revue](revue-v1.md) (Constats mineurs, Corrections).
  - Décision :

- [ ] **Vérifications jamais faites** — Les planifier ?
  - Contexte : liens profonds sur une version de développement (le schéma `uzu://` n'existe pas dans Expo Go) ; comportement d'Expo face à un jeton d'appareil mal formé dans un lot ; plans d'exécution des recherches géographiques sur un grand volume.
  - Source : [revue](revue-v1.md) (Ce qui n'a pas pu être vérifié).
  - Décision :

---

## 🏗️ 6. Infrastructure et mise en ligne : choix ouverts

- [ ] **Hébergeur de production** — Quel hébergeur ?
  - Contexte : non choisi. Contraintes : petite instance, PostgreSQL avec PostGIS, volume persistant pour les photos, une seule instance, données dans l'Union européenne. Recommandation du guide : option A, serveur Hetzner Cloud avec Docker Compose (environ 12 à 15 € par mois, système et sauvegardes à la charge du porteur). Les prix datent des 18 et 19/11/2026 et sont à relire à la commande ; ceux de Render et d'Azure n'ont pas pu être lus.
  - Options : A Hetzner (recommandée) ; B Railway (rien à administrer, une seule région européenne, Amsterdam) ; C Render (Francfort, prix non vérifiés) ; D Fly.io (environ 45 $ avec base gérée) ; E Scaleway (si la France est exigée) ; F Azure (le plus lourd). Scalingo écartée (disque temporaire).
  - Sources : [déploiement §7](deploiement.md), [revue](revue-v1.md), [plan](plan.md).
  - Décision :

- [ ] **Nom de domaine public de l'API** — Quel nom ? (déduit)
  - Contexte : la liste de contrôle exige `AllowedHosts` égal au nom public, un TLS valide et l'adresse de l'API dans l'application mobile ; aucun nom n'est cité dans les documents lus.
  - Sources : [déploiement](deploiement.md) (section 6), [README mobile](../mobile/README.md).
  - Décision :

- [ ] **Sauvegardes et restauration** — Quel stockage hors du serveur pour les copies quotidiennes de la base et des photos ?
  - Contexte : la liste de contrôle demande des sauvegardes copiées hors du serveur et une restauration **essayée** ; le guide ne désigne pas de stockage.
  - Source : [déploiement](deploiement.md) (sections 4 et 6).
  - Décision :

- [ ] **Surveillance et plan de reprise** — Quel service de sondes (UptimeRobot, Better Stack, Healthchecks.io…) et quel plan si l'API tombe un soir de week-end ?
  - Contexte : sonde sur `/api/v1/health` avec une alerte qui arrive ; plan de reprise écrit, durée de restauration mesurée. Le guide dit de choisir le service sur leurs sites officiels.
  - Source : [déploiement](deploiement.md) (sections 5 et 6).
  - Décision :

- [ ] **Liste de contrôle avant d'ouvrir aux testeurs (côté API)** — La passer entièrement avant le premier testeur ?
  - Contexte : 14 cases dont `Production` sans connexion de test, clé de signature propre au déploiement (copie dans le gestionnaire de mots de passe), `AllowedHosts`, TLS et HSTS, `Proxy__Enabled`, notification réelle reçue, connexions Apple et Google réelles, `Admin__Emails`, photo qui survit à un redémarrage, pare-feu.
  - Source : [déploiement §6](deploiement.md).
  - Décision :

- [ ] **Démo hébergée : garder, fermer, ou remplacer** — Combien de temps la garder, et que faire de ses limites ?
  - Contexte : en ligne depuis le 22/11/2026 (Render, Supabase, cron-job.org), ouverte à quiconque connaît l'adresse, données fictives seulement, `Admin:Emails` à garder vide ; la build iPhone à Apple ID gratuit cesse de s'ouvrir après 7 jours (réinstaller depuis le Mac). Le guide explique comment tout arrêter.
  - Sources : [démo hébergée](demo-hebergee.md), [ADR 0043](adr/0043-demo-hebergee.md), [README mobile](../mobile/README.md).
  - Décision :

- [ ] **Démo hébergée : points restés « non vérifiés »** — Les vérifier en ligne et corriger le guide ?
  - Contexte : le ping suffit-il à éviter la pause de la base Supabase ; réseau et `X-Forwarded-For` du proxy de Render (donc `Proxy__*` non posé, limite de débit partagée) ; Render demande-t-il une carte bancaire ; `Trust Server Certificate=true` et `geography` avec PostGIS dans le schéma `extensions` ; noms exacts des boutons ; Supabase Storage seulement essayé via MinIO local.
  - Sources : [démo hébergée §7](demo-hebergee.md), [ADR 0043](adr/0043-demo-hebergee.md).
  - Décision :

---

## Hors de ce document

- Le détail du code, des tests et des commits : voir [`plan.md`](plan.md) et [`revue-v1.md`](revue-v1.md).
- Les ADR déjà « Accepté » sans note de délégation : aucune action attendue.
- Les décisions déjà prises par le porteur et appliquées (nom Uzu, zone pilote, connexion Apple ou Google seulement, paiement hors application en V1, organisations validées à la main, suspension « geler, annuler, laisser partir », blocage indiscernable, organisation gelée sans annulation) : voir [`perimetre-v1.md`](perimetre-v1.md) et la revue.
- La marche à suivre pas à pas des comptes et des builds : [README mobile](../mobile/README.md), [déploiement](deploiement.md), [démo hébergée](demo-hebergee.md).
- Le dépôt `DogoMeet` : conservé tel quel, à part.
- Les copies de travail de `.claude/worktrees/` : ignorées.
