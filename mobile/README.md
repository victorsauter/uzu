# Uzu — application mobile

Application iOS et Android d’Uzu (le nom du produit, voir l’ADR 0041 ; le dépôt et le dossier gardent le nom `dogo`), en React Native avec Expo (SDK 58, React Native 0.88, React 19.3), TypeScript strict et Expo Router.

## Prérequis

- Node.js 22.13 ou plus récent (développé avec Node 26 et npm 11, utilisés aussi par la CI).
- Pour le simulateur iOS : Xcode 26.4 ou plus récent, sur macOS.
- Pour l'émulateur Android : Android Studio et un appareil virtuel.
- Pour un téléphone : l'application Expo Go (App Store ou Play Store).
- L'API Uzu lancée en local (voir `api/README.md`), sur `http://localhost:5080` par défaut.

L'application se lance et s'utilise entièrement dans **Expo Go et le simulateur iOS** avec la connexion de test. Ce qui demande une **version de développement** (`expo prebuild` ou EAS, voir « Construire une version de test » plus bas ; pas encore construite) :

- la **connexion Google** (`@react-native-google-signin/google-signin`, code natif absent d'Expo Go) ; sans elle, ou sans identifiants, le bouton est masqué ;
- les **notifications push** : sans projet EAS (`EXPO_PUBLIC_EAS_PROJECT_ID`), elles sont indisponibles et l'application l'ignore sans rien afficher (voir « Notifications » plus bas et l'[ADR 0022](../docs/adr/0022-notifications-push-mobile.md)).

La **connexion Apple** (`expo-apple-authentication`) fonctionne dans Expo Go sur iOS (le bouton est masqué sur Android) ; elle ne réussira pourtant qu'une fois l'API configurée avec un identifiant d'audience Apple (`Auth:Apple:Audiences`). Appareil photo, photothèque, sélecteur de date, cartes (`react-native-maps` : Apple Plans sur iOS, Google Maps sur Android), position (`expo-location`), menus natifs d’iOS (`@expo/ui`), verre liquide d’iOS 26 (`expo-glass-effect`), flou (`expo-blur`) et retour haptique (`expo-haptics`) fonctionnent dans Expo Go. Une version Android publiée demande une clé Google Maps (variable `GOOGLE_MAPS_ANDROID_API_KEY`, voir « Construire une version de test » et l'[ADR 0021](../docs/adr/0021-carte-et-position-mobile.md)). Voir l'[ADR 0018](../docs/adr/0018-connexion-native-chargee-a-la-demande.md).

## Installation

```sh
cd mobile
npm ci
cp .env.example .env   # puis adapter les valeurs
```

npm 11 bloque par défaut les scripts d'installation et affiche un avertissement pour `fsevents` et `@parcel/watcher` : ces paquets sont facultatifs et l'application fonctionne sans ces scripts.

## Lancer l'application

```sh
npm start          # serveur Metro ; puis i (iOS), a (Android) ou QR code pour Expo Go
npm run ios        # ouvre directement le simulateur iOS
npm run android    # ouvre directement l'émulateur Android
```

- **Simulateur iOS** : `localhost` désigne l'ordinateur, la valeur par défaut convient.
- **Émulateur Android** : `localhost` désigne l'émulateur ; mettre `EXPO_PUBLIC_API_URL=http://10.0.2.2:5080`, ou rediriger les ports (`adb reverse tcp:5080 tcp:5080` et `adb reverse tcp:8081 tcp:8081`) et garder `localhost`, ce qui s'est montré plus fiable. Lancer l'émulateur avec `-gpu swiftshader_indirect` si la carte reste grise.
- **Téléphone réel** : sur le même réseau Wi-Fi, mettre l'adresse IP de l'ordinateur, par exemple `http://192.168.1.20:5080`.

Après un changement de `.env`, relancer Metro avec `npx expo start --clear`.

## Variables d'environnement

Lues une seule fois dans `src/lib/config.ts` (objet `config` typé). Expo intègre les variables `EXPO_PUBLIC_*` au moment de la construction : elles ne sont pas secrètes et ne doivent jamais contenir de secret.

| Variable                                                        | Défaut                                            | Rôle                                                                                                                                                                           |
| --------------------------------------------------------------- | ------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `EXPO_PUBLIC_API_URL`                                           | `http://localhost:5080` (développement seulement) | Adresse de l'API, sans `/api/v1` ; obligatoire et en `https` dans une version de distribution (voir plus bas)                                                                  |
| `EXPO_PUBLIC_DEV_LOGIN`                                         | absente                                           | `true` affiche, **dans une version de développement seulement** (`__DEV__`), la connexion de test (l'API doit avoir `Auth:AllowDevLogin=true`) et les données de démonstration |
| `EXPO_PUBLIC_DEFAULT_LATITUDE`, `EXPO_PUBLIC_DEFAULT_LONGITUDE` | Seppois-le-Haut (`47.5339`, `7.1844`)             | Centre de la carte quand la position n'est pas connue ; les deux sont nécessaires, une valeur invalide est ignorée                                                             |
| `EXPO_PUBLIC_GOOGLE_WEB_CLIENT_ID`                              | absente                                           | Identifiant client OAuth web de Google ; sans lui, bouton Google masqué (version de développement requise)                                                                     |
| `EXPO_PUBLIC_GOOGLE_IOS_CLIENT_ID`                              | absente                                           | Identifiant client OAuth iOS, requis en plus sur iOS ; il sert aussi au plugin natif (`app.config.ts`)                                                                         |
| `EXPO_PUBLIC_EAS_PROJECT_ID`                                    | absente                                           | Identifiant du projet EAS (`eas init`) : sans lui, notifications push indisponibles (`extra.eas.projectId`)                                                                    |
| `GOOGLE_MAPS_ANDROID_API_KEY`                                   | absente                                           | Clé Google Maps pour Android, lue par `app.config.ts` à la construction (sans préfixe `EXPO_PUBLIC_`) ; sans elle, carte grise hors Expo Go                                    |
| `GOOGLE_SERVICES_JSON`                                          | absente                                           | Chemin de `google-services.json` (Firebase), nécessaire aux notifications push sur Android ; sur EAS, variable de type fichier                                                 |

**Version de distribution** (`__DEV__` faux : build de prévisualisation ou de production) : le repli sur `http://localhost:5080` n'existe pas. Sans `EXPO_PUBLIC_API_URL`, ou avec une adresse qui n'est pas en `https`, l'application affiche au démarrage l'écran « Application mal configurée » (`AppProviders`, `config.error`) au lieu de tenter des requêtes vouées à l'échec. La connexion de test et les données de démonstration n'y apparaissent jamais, même avec `EXPO_PUBLIC_DEV_LOGIN=true`. Pour voir cet écran : `npx expo start --no-dev --minify` sans la variable.

## Données de démonstration (développement)

Avec `EXPO_PUBLIC_DEV_LOGIN=true`, l'écran de connexion montre, sous « Connexion de test », deux cartes réservées au développement (voir l'[ADR 0027](../docs/adr/0027-donnees-de-demonstration.md) et « Données de démonstration » dans `api/README.md`) :

- **« Données de démonstration »** : l'état (aucune donnée, ou le nombre de comptes, chiens, balades, messages et photos), l'interrupteur « Télécharger des photos » (activé par défaut), « Générer les données de démonstration » (avec les photos, compter quelques secondes à une minute : le bouton tourne et une phrase l'explique, puis un résumé s'affiche) et « Supprimer les données de démonstration » après confirmation. Générer à nouveau remplace les données de démonstration ; le compte de test (`dev-user-1`) n'est jamais touché. Si l'API répond 404 (`DemoData:Enabled` à `false`), la carte le dit en une phrase.
- **« Se connecter comme… »** : les comptes de démonstration avec leur avatar, leurs chiens, le badge « Éducateur » et leur organisation, et un champ de recherche (nom, chien ou organisation, sans tenir compte des accents). Toucher un compte ouvre une session par la connexion de test, avec son `subject`.

Pour essayer : lancer l'API en Development (`DemoData:Enabled` y est vrai), ouvrir l'application déconnectée, générer, puis se connecter comme `demo-marie-schmitt` (balade complète, fils de discussion), `demo-thomas-haas` (avertissement « Participant bloqué ») ou `demo-sandrine-gutzwiller` (responsable d'organisation : membres et demandes). Se déconnecter ramène à l'écran de connexion pour changer de compte.

Le code vit à part, dans `src/devtools/` (`demoData.ts` pour les appels typés depuis `schema.d.ts`, `DemoDataSection.tsx` pour l'interface) ; `loadDemoDataSection()` ne le charge que dans une version de développement avec `EXPO_PUBLIC_DEV_LOGIN` à `true` : sinon rien n'est rendu ni appelé. Les photos viennent de DiceBear (avatars dessinés, CC0) et de Wikimedia Commons (photos de races sous licence libre) ; elles restent sur l'API de développement.

## Construire une version de test

Cette section dit comment produire une build installable sur un vrai téléphone : TestFlight sur iOS, test interne sur Android. Elle est écrite pour une première fois, avec EAS (Expo Application Services). La configuration est prête (`eas.json`, `app.config.ts`) ; **rien n'a encore été construit ni envoyé** : il manque les comptes, qui ne peuvent être créés que par le porteur du produit (voir l'[ADR 0039](../docs/adr/0039-profils-de-build-eas.md)).

> **Identifiants confirmés (ADR 0041).** Nom affiché `Uzu`, slug Expo `uzu`, identifiant de bundle iOS et nom de paquet Android `com.victorsauter.uzu`, schéma de lien `uzu://` (`uzu://walks/<id>`). Ils sont **figés dès la première build envoyée** à App Store Connect ou à Google Play : on ne les change plus ensuite sans créer une nouvelle application. Se fabriquer des builds de développement ou `preview` (installées par lien ou par câble) reste sans conséquence. Aucune build n'a jamais été distribuée : l'ancien schéma `dogo://` n'est plus accepté, et les clés de stockage `dogo.*` de l'appareil ne sont pas reprises (`uzu.*`).

### Les profils de build (`eas.json`)

| Profil                  | Usage                                                                                                                             | Distribution                    | Environnement EAS | Numéro de build |
| ----------------------- | --------------------------------------------------------------------------------------------------------------------------------- | ------------------------------- | ----------------- | --------------- |
| `development`           | Client de développement sur un appareil (iOS : ad hoc, appareil à enregistrer ; Android : `.apk`)                                 | interne                         | `development`     | non incrémenté  |
| `development-simulator` | Client de développement pour le simulateur iOS                                                                                    | interne                         | `development`     | non incrémenté  |
| `preview`               | Test par les testeurs. iOS : build « store » envoyée à **TestFlight** ; Android : `.apk` installable par un lien, sans Play Store | iOS : store ; Android : interne | `preview`         | incrémenté      |
| `production`            | Les stores. iOS : TestFlight puis App Store ; Android : `.aab` pour Google Play                                                   | store                           | `production`      | incrémenté      |

- **Version.** Le numéro de version affiché (`0.1.0`) vient de `app.config.ts`. Le numéro de build iOS (`buildNumber`) et le code de version Android (`versionCode`) sont tenus par EAS (`"appVersionSource": "remote"`) et augmentent tout seuls à chaque build `preview` et `production` ; ils ne sont écrits nulle part dans le dépôt. La première build demande de les initialiser : accepter les valeurs proposées.
- **Pourquoi `preview` iOS passe par TestFlight.** La distribution « interne » d'EAS sur iOS est une distribution ad hoc : chaque iPhone doit être enregistré (`eas device:create`) avant la build, 100 appareils par an au plus. TestFlight évite cela : les testeurs reçoivent l'application depuis l'application TestFlight.
- **Canal.** `channel` (`preview`, `production`) n'a d'effet qu'avec `expo-updates` (mises à jour sans passer par les stores), qui n'est pas installé : il est prêt pour plus tard. Si EAS CLI s'en plaint, supprimer ces deux lignes.
- **`submit`.** Les champs `appleId`, `appleTeamId` et `ascAppId` valent `A_REMPLIR_…` : les remplacer (voir plus bas). Tant qu'ils n'ont pas une forme valide, `eas submit` pour iOS refuse de partir, avec un message qui dit lequel corriger. Le profil `closed-test` envoie un `.aab` sur la piste de test fermé de Google Play.

### Ce que ça coûte

| Poste                                | Coût                                                                                                                                                                                                   | À savoir                                                                                            |
| ------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------- |
| Compte Apple Developer Program       | 99 USD par an                                                                                                                                                                                          | indispensable pour TestFlight, pour la connexion Apple dans une build et pour les notifications iOS |
| Compte Google Play Console           | 25 USD, une seule fois                                                                                                                                                                                 | vérification d'identité (pièce d'identité, carte bancaire)                                          |
| EAS Build et EAS Submit              | formule gratuite : jusqu'à 15 builds Android et 15 iOS (page des tarifs d'Expo ; le quota semble mensuel, à vérifier), file d'attente à basse priorité ; ensuite à partir de 19 USD par mois (Starter) | chaque `eas build` consomme ce quota                                                                |
| Firebase (notifications Android)     | gratuit (FCM)                                                                                                                                                                                          | un projet Firebase est nécessaire                                                                   |
| Google Maps Platform (carte Android) | un compte de facturation Google Cloud avec une carte bancaire est demandé pour obtenir la clé, même si l'usage reste dans la gratuité ; vérifier la grille tarifaire                                   |                                                                                                     |
| Hébergement de l'API                 | hors de cette section (voir `api/README.md`)                                                                                                                                                           |                                                                                                     |

Aucune de ces commandes ne doit être lancée par un agent : elles utilisent les comptes du porteur et consomment son quota.

### Marche à suivre, dans l'ordre

Tout se fait depuis `mobile/`. Installer l'outil une fois : `npm install -g eas-cli` (version 27.10 ou plus récente, vérifiée au moment de l'écriture).

**1. Créer les comptes** (par le porteur, avec ses papiers) :

1. un compte **Expo** (gratuit) sur <https://expo.dev> ;
2. un compte **Apple Developer Program** (99 USD par an), plusieurs jours d'attente possibles ; noter l'**identifiant d'équipe** (Team ID, 10 caractères) ;
3. un compte **Google Play Console** (25 USD), avec la vérification d'identité, et la vérification d'un appareil Android par l'application Play Console, que Google exige d'un compte personnel avant toute publication ;
4. un projet **Google Cloud** (identifiants de connexion et clé Maps) et un projet **Firebase** (notifications Android).

**2. Créer le projet EAS et reporter son identifiant.**

```sh
eas login                       # le porteur saisit lui-même son mot de passe
eas init                        # crée le projet uzu sur expo.dev (ou : eas init --id <id> pour un projet existant)
```

Le projet Expo doit avoir le slug `uzu` (celui de `app.config.ts`) et appartenir au compte du porteur.

`app.config.ts` est dynamique : EAS CLI ne peut pas y écrire l'identifiant, il l'affiche (il est aussi sur la page du projet, sur expo.dev). Il se reporte dans la variable `EXPO_PUBLIC_EAS_PROJECT_ID` (voir l'étape suivante). Le temps des premières commandes, la définir aussi dans le terminal pour que EAS CLI trouve le projet : `export EXPO_PUBLIC_EAS_PROJECT_ID=<id>` (non vérifié sans compte : si EAS CLI la réclame ailleurs, la poser aussi dans `mobile/.env`, qui n'est pas versionné).

**3. Poser les variables d'environnement EAS.** `.env` n'est pas versionné, donc EAS Build ne le voit pas : les variables d'une build viennent des **variables d'environnement EAS** du projet (mécanisme recommandé par Expo), rangées par environnement (`development`, `preview`, `production`) ; chaque profil de `eas.json` désigne le sien (`"environment"`). Aucune valeur n'est dans le dépôt.

| Variable                                                        | `development`                                               | `preview`                | `production`             | Visibilité |
| --------------------------------------------------------------- | ----------------------------------------------------------- | ------------------------ | ------------------------ | ---------- |
| `EXPO_PUBLIC_API_URL`                                           | adresse de l'API joignable depuis l'appareil (`http` admis) | adresse `https` de l'API | adresse `https` de l'API | texte brut |
| `EXPO_PUBLIC_EAS_PROJECT_ID`                                    | identifiant du projet                                       | idem                     | idem                     | texte brut |
| `EXPO_PUBLIC_GOOGLE_WEB_CLIENT_ID`                              | client web Google                                           | idem                     | idem                     | texte brut |
| `EXPO_PUBLIC_GOOGLE_IOS_CLIENT_ID`                              | client iOS Google                                           | idem                     | idem                     | texte brut |
| `GOOGLE_MAPS_ANDROID_API_KEY`                                   | clé Maps Android                                            | idem                     | idem                     | sensible   |
| `GOOGLE_SERVICES_JSON` (type **fichier**)                       | `google-services.json`                                      | idem                     | idem                     | sensible   |
| `EXPO_PUBLIC_DEV_LOGIN`                                         | `true` si on veut la connexion de test                      | **absente**              | **absente**              | texte brut |
| `EXPO_PUBLIC_DEFAULT_LATITUDE`, `EXPO_PUBLIC_DEFAULT_LONGITUDE` | facultatives                                                | facultatives             | facultatives             | texte brut |

Les variables `EXPO_PUBLIC_*` sont intégrées au code de l'application : tout le monde peut les lire, ce ne sont jamais des secrets (Expo le dit : une valeur « secrète » n'y apporterait aucune protection). `GOOGLE_MAPS_ANDROID_API_KEY` et `GOOGLE_SERVICES_JSON` n'ont pas le préfixe `EXPO_PUBLIC_` : `app.config.ts` les lit à la construction, elles n'entrent pas dans le JavaScript (mais la clé Maps finit lisible dans l'application, voir plus bas).

```sh
# exemples (une commande par valeur ; --environment peut être répété)
eas env:set --name EXPO_PUBLIC_API_URL --value https://api.exemple.fr --environment preview --environment production --visibility plaintext
eas env:set --name EXPO_PUBLIC_EAS_PROJECT_ID --value <id> --environment development --environment preview --environment production --visibility plaintext
eas env:set --name GOOGLE_SERVICES_JSON --type file --value ./google-services.json --environment development --environment preview --environment production --visibility sensitive
eas env:list --environment preview        # vérifier
```

(Les options exactes de `eas env:set` sont dans `eas env:set --help` ; les mêmes réglages se font aussi sur expo.dev, page « Environment variables » du projet.) Pour voir ce que donnera la configuration d'un profil, sans lancer de build :

```sh
eas env:exec preview "EAS_BUILD_PROFILE=preview npx expo config --type public"
```

**Garde-fou.** Pour les profils `preview` et `production`, `app.config.ts` fait échouer la build, avec un message qui dit quoi corriger, si `EXPO_PUBLIC_API_URL` est absente ou n'est pas en `https`, ou si `EXPO_PUBLIC_DEV_LOGIN` vaut `true`. Il s'appuie sur `EAS_BUILD_PROFILE`, que EAS Build pose sur la machine de build : EAS CLI ne l'a pas quand il lit la configuration chez toi, donc l'erreur peut n'apparaître qu'à l'étape « Read app config » du journal de build (la commande `eas env:exec` ci-dessus la montre avant). Le développement local et les tests ne sont jamais gênés (`app.config.ts`, tests dans `src/lib/__tests__/appConfig.test.ts`).

**4. Créer les identifiants de connexion Apple et Google, et les reporter des deux côtés.** Tout est lié à l'identifiant `com.victorsauter.uzu` : l'audience Apple de l'API (`Auth__Apple__Audiences__0`) est exactement cet identifiant, et les clients OAuth Google (iOS, Android) et la clé Google Maps Android se créent pour ce bundle et ce paquet.

| Fournisseur | À créer                                                                                                                                                                                                                              | À reporter côté application                                                                        | À reporter côté API (`api/.env.example`)                                                                                 |
| ----------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| Apple       | rien d'autre que le compte : EAS crée l'identifiant d'application, le certificat et active la capacité « Se connecter avec Apple » à la première build iOS (à vérifier)                                                              | rien                                                                                               | `Auth__Apple__Audiences__0` = l'identifiant de bundle (`com.victorsauter.uzu`, déjà dans le fichier d'exemple)           |
| Google      | dans la console Google Cloud, écran de consentement OAuth, puis trois identifiants OAuth : un **client Web**, un **client iOS** (identifiant de bundle) et un **client Android par empreinte de certificat** (nom de paquet + SHA-1) | `EXPO_PUBLIC_GOOGLE_WEB_CLIENT_ID` (client Web) et `EXPO_PUBLIC_GOOGLE_IOS_CLIENT_ID` (client iOS) | `Auth__Google__Audiences__0` = l'identifiant du **client Web** (c'est lui, l'audience du jeton envoyé par l'application) |

- Le client Android ne s'écrit nulle part dans le code : Google s'en sert pour reconnaître l'application qui ouvre la fenêtre de connexion. Sans lui, la connexion échoue sur Android (erreur `DEVELOPER_ERROR`). Il en faut **un par empreinte SHA-1** : celle du keystore géré par EAS (pour `development` et `preview` ; `eas credentials -p android`, ou expo.dev, projet, Credentials) et celle de la **clé de signature de l'application de Google Play** (Play Console, Intégrité de l'application) pour les builds installées depuis le Play Store.
- L'API doit avoir `Auth__AllowDevLogin=false` et `DemoData__Enabled=false` hors développement (elle refuse de démarrer sinon) ; elle est déployée par un autre lot.
- Après un renommage de l'application, l'audience Apple de l'API doit suivre le nouvel identifiant de bundle.

**5. Notifications push.**

- **iOS** : à la première build, EAS CLI propose de configurer les notifications et de créer une clé APNs : répondre oui (compte Apple payant exigé).
- **Android** : ajouter une application Android dans le projet Firebase (nom de paquet `com.victorsauter.uzu`), télécharger `google-services.json` (variable `GOOGLE_SERVICES_JSON`, étape 3), puis créer une clé de compte de service (Firebase, Paramètres, Comptes de service, « Générer une nouvelle clé privée ») et la téléverser dans EAS : `eas credentials`, Android, puis la clé de compte de service pour les notifications (FCM V1) ; ou sur expo.dev, Project settings, Credentials. Cette clé ne va jamais dans le dépôt (le `.gitignore` de `mobile/` ignore `google-service-account*.json` et `google-services.json`). Si la clé d'API Google Cloud du projet Firebase est restreinte, autoriser « FCM Registration API » et « Firebase Installations API », avec l'empreinte SHA-1 de la clé de signature de Google Play.

**6. Clé Google Maps pour Android.** Sans elle, la carte reste grise hors Expo Go. Dans la console Google Cloud : activer **Maps SDK for Android**, créer une clé d'API, puis **la restreindre** : restriction d'application « Applications Android », avec le nom de paquet (`com.victorsauter.uzu`) et une entrée par empreinte SHA-1 (celle du keystore EAS et celle de la clé de signature de Google Play, comme ci-dessus), et restriction d'API limitée à Maps SDK for Android. Une clé embarquée dans une application est lisible par n'importe qui qui l'ouvre : ce sont ces restrictions qui la protègent. La clé se pose dans `GOOGLE_MAPS_ANDROID_API_KEY` ; `app.config.ts` l'ajoute par l'option `androidGoogleMapsApiKey` du plugin de `react-native-maps` (Apple Plans sur iOS n'a besoin d'aucune clé).

**7. Construire et envoyer sur iOS (TestFlight).**

```sh
# Remplir d'abord eas.json, section submit : appleId, appleTeamId et ascAppId.
# ascAppId = « Identifiant Apple » de l'app dans App Store Connect, Informations sur l'app.
# Tant que la fiche n'existe pas, supprimer la ligne ascAppId : EAS crée la fiche à l'envoi.
eas build -p ios --profile preview                 # la première fois, EAS CLI demande la connexion Apple (le porteur la saisit, avec son code de vérification)
eas submit -p ios --profile preview --latest       # envoie la dernière build à App Store Connect
# ou, en une commande :
eas build -p ios --profile preview --auto-submit
```

Le traitement par Apple prend en général 10 à 15 minutes ; la build apparaît ensuite dans App Store Connect, onglet TestFlight. Les **testeurs internes** (100 au plus) sont des utilisateurs de l'équipe App Store Connect (Utilisateurs et accès) : ils reçoivent la build sans examen, depuis l'application TestFlight. Les **testeurs externes** (10 000 au plus, par lien ou par e-mail) exigent un examen de la première build par Apple. Une build reste testable 90 jours. La question du chiffrement à l'envoi ne se pose plus (`usesNonExemptEncryption: false`).

**8. Construire et envoyer sur Android.**

```sh
eas build -p android --profile preview             # .apk : EAS donne un lien et un QR code, à ouvrir sur le téléphone, sans Play Store
eas build -p android --profile production          # .aab, pour Google Play
eas submit -p android --profile production --latest   # piste « test interne », en brouillon
eas submit -p android --profile closed-test --latest  # piste « test fermé » (alpha)
```

- Avant `eas submit`, dans Play Console : créer l'application (« Créer une application »), créer une clé de compte de service dans Google Cloud, l'inviter dans Play Console (Utilisateurs et autorisations) avec le droit de publier, et la téléverser dans EAS (`eas credentials -p android` ou expo.dev, Credentials). Ensuite terminer les tâches de configuration de la fiche (politique de confidentialité, classification du contenu, sécurité des données, accès à l'application). **Les textes légaux de l'application sont encore une version provisoire** (`src/strings/legal.ts`) : ils sont à faire valider et héberger avant toute fiche publique.
- Les envois sont en `releaseStatus: draft` : la version est déposée sans être déployée, à finir dans Play Console. Une fois la fiche complète, passer à `completed` dans `eas.json` si on veut un déploiement direct. La documentation d'EAS dit que la première soumission passe par `eas submit` ; si Google la refuse, télécharger le `.aab` depuis expo.dev et le déposer à la main dans Play Console, Test interne.
- **Test interne** : 100 testeurs au plus, disponible en quelques minutes, sans condition.
- **Test fermé obligatoire avant la production.** Pour un compte personnel récent, Google exige, avant de pouvoir demander l'accès à la production, un test fermé avec **au moins 12 testeurs inscrits sans interruption pendant 14 jours** (règle de la page d'aide Google « Test requirements for new personal developer accounts », vérifiée à la rédaction : à relire au moment de s'en servir, Google la fait évoluer). Le test interne ne la remplace pas. Il faut donc recruter 12 testeurs (adresses Google) et ouvrir le test fermé (`closed-test`) **le plus tôt possible**, sans attendre une application terminée : la durée de 14 jours est incompressible.

**9. Ce qu'il faut vérifier sur la première build.**

1. **Démarrage** : pas d'écran « Application mal configurée » ; l'écran de connexion n'affiche **pas** « Connexion de test » ni les données de démonstration.
2. **Connexion Apple** (iPhone) puis **connexion Google** (iPhone et Android) : un compte se crée, le profil s'ouvre ; l'API répond sans refuser le jeton (sinon, vérifier les audiences de `api/.env.example`).
3. **Carte Android** : les tuiles s'affichent (grise = clé Maps absente, mal restreinte, ou empreinte SHA-1 manquante dans la restriction). Sur iOS, la carte Apple Plans.
4. **Notifications** : accepter l'autorisation proposée après une inscription à une balade ; depuis un second compte, écrire dans le fil de la balade ; la notification arrive application fermée, et un appui ouvre le fil. À faire sur iOS et sur Android (sans `google-services.json` et clé FCM, Android ne reçoit rien).
5. **Liens profonds** : ouvrir `uzu://walks/<id>` (simulateur : `xcrun simctl openurl booted uzu://walks/<id>` ; Android : `adb shell am start -a android.intent.action.VIEW -d "uzu://walks/<id>"`) ; un lien inconnu (`uzu://nimporte`) affiche la page « introuvable ».
6. **Position, photos, appareil photo** : les trois demandes d'autorisation apparaissent avec leurs textes en français.
7. **Mode sombre sur Android** : `expo-doctor` ne le signale pas, mais le prebuild avertit que `userInterfaceStyle` demande `expo-system-ui` sur Android ; à vérifier sur la build, et à installer au besoin.
8. **Icône, écran de démarrage** : la tête de Jack Russell sur fond sable (voir « Logo, icônes et écran de démarrage ») ; sur Android, l'icône à thème (Android 13+) montre la silhouette d'une seule couleur, et l'écran de démarrage n'est fidèle que sur une build de distribution (pas dans Expo Go).

### Client de développement

Pour essayer le code natif (connexion Google, notifications) avec le serveur de développement :

```sh
eas build -p ios --profile development-simulator   # simulateur iOS, rien à enregistrer
eas build -p ios --profile development             # iPhone : l'appareil doit d'abord être enregistré (eas device:create)
eas build -p android --profile development         # .apk
npx expo start --dev-client
```

### Si l'application devait être renommée à nouveau

Le renommage en Uzu (ADR 0041) a suivi ce chemin ; il ne se refait pas après le premier envoi à un store. Tout part de `APP` en tête de `app.config.ts` (nom affiché, slug, schéma de lien, identifiants de bundle et de paquet) ; les textes des permissions le reprennent. Le schéma est aussi écrit dans `src/lib/appLinks.ts` (`APP_URL_SCHEME`, lu par `src/notifications/routing.ts`), qu'un test de `appConfig.test.ts` tient égal à `APP.urlScheme`, et dans l'API (liens des notifications). Ensuite, à la main : `Auth__Apple__Audiences__0` de l'API, les clients OAuth Google (identifiant de bundle et nom de paquet), l'application Android du projet Firebase (nom de paquet), la restriction de la clé Maps (nom de paquet), le projet EAS (slug), `fr.app.name` et les textes de `src/strings/` qui citent le nom, et les clés de stockage `uzu.*`.

## Scripts

| Commande                                  | Rôle                                                        |
| ----------------------------------------- | ----------------------------------------------------------- |
| `npm run typecheck`                       | Vérification des types (`tsc --noEmit`)                     |
| `npm run lint`                            | ESLint (configuration Expo, `eslint-config-prettier`)       |
| `npm run format` / `npm run format:check` | Prettier : corriger / vérifier                              |
| `npm test`                                | Tests Jest (`jest-expo`, React Native Testing Library 14)   |
| `npm run api:generate`                    | Régénère `src/api/schema.d.ts` depuis `../api/openapi.json` |
| `npm run brand:export`                    | Réexporte les icônes et l'écran de démarrage depuis le logo |

La CI (`.github/workflows/mobile.yml`) lance `npm ci`, puis types, lint, format et tests.

**Environnement des tests.** Jest tourne toujours en `TZ=Europe/Paris` (`jest.global-setup.js`), quel que soit le fuseau de la machine : les tests de dates couvrent les passages à l'heure d'été (29 mars 2026) et d'hiver (25 octobre 2026). Les minuteries de ramasse-miettes de TanStack Query (`gcTime`, 5 min) ne retiennent plus le processus (`jest.setup.ts`, `timeoutManager`) : sans cela Jest signalait « A worker process has failed to exit gracefully ». Un test qui lance une vraie minuterie longue doit la nettoyer.

## Régénérer le client d'API

```sh
npm run api:generate
npm run typecheck
```

Le script lit `../api/openapi.json` et échoue avec un message clair s'il n'existe pas. `src/api/schema.d.ts` est généré depuis `api/openapi.json` (lot 1 de l'API). À chaque changement de `api/openapi.json`, régénérer et versionner `schema.d.ts` dans la même demande de fusion.

## Structure

```text
mobile/
├── app.config.ts          nom, identifiants, schéma uzu://, textes des permissions, garde-fou de distribution
├── eas.json               profils de build EAS (development, preview, production) et soumission
├── scripts/generate-api.mjs
└── src/
    ├── app/               écrans (Expo Router) — uniquement des routes et des dispositions
    │   ├── _layout.tsx    fournisseurs + garde d'authentification + `ErrorBoundary` (écran d'erreur en français, « Réessayer »)
    │   ├── +not-found.tsx adresse inconnue (`uzu://nimporte`) : écran en français, retour à l'accueil
    │   ├── +native-intent.ts liens reçus : un lien d'invitation est gardé en attente jusqu'à la connexion
    │   ├── (auth)/        écrans accessibles déconnecté (sign-in)
    │   ├── (legal)/       conditions d'utilisation et confidentialité, sans garde (ADR 0025)
    │   └── (app)/         pile : (tabs)/ (Balades, Mes balades `mine`, Profil) et écrans poussés
    │       ├── edit-profile, users/[id], dogs/new, dogs/[id]/{index,edit,invite}, invitation/[token]
    │       ├── walks/new, walks/[id]/{index,edit,messages}, notifications
    │       ├── organizations/{index,new}, organizations/[id]/{index,edit,members}
    │       └── blocked, report, delete-account
    ├── api/               client typé, erreurs, cache, photos, énumérations, hooks (profile, dogs, walks, organisations, blocages, signalements, compte)
    ├── notifications/     notifications push : chargement conditionnel, enregistrement de l'appareil, liens, écoute
    ├── auth/              session (magasin sécurisé, SessionProvider, useSession), signIn, providers/ (Apple, Google)
    ├── components/        composants de base (Screen, Text, Button, TextField…)
    ├── discover/          les deux vues de l'onglet Balades (panneau, liste), leur préférence et le réglage du profil
    ├── theme/             couleurs claires et sombres (charte « Uzu »), espacements, rayons, typographie
    ├── strings/           tous les textes français (fr.ts)
    ├── lib/               configuration, fournisseurs, âge, dates, géographie, position, validation des formulaires, sélecteur de photo
    ├── devtools/          outils de développement (données de démonstration), chargés seulement avec EXPO_PUBLIC_DEV_LOGIN=true
    └── test/              utilitaires de test (pas de tests ici)
```

Les tests vivent dans des dossiers `__tests__/` à côté du code testé, jamais dans `src/app/` (tout fichier de `src/app/` est une route).

## Conventions

### Ajouter un écran

1. Créer le fichier dans `src/app/(app)/` (connecté ; un écran de détail ou de formulaire va là, un onglet dans `(app)/(tabs)/`, voir l'[ADR 0017](../docs/adr/0017-ecrans-empiles-sur-les-onglets.md)) ou `src/app/(auth)/` (déconnecté). Exemple : `src/app/(app)/walks/[id].tsx` donne `/walks/123` et `uzu://walks/123`.
2. L'envelopper dans `<Screen>` ; `scroll={false}` pour une liste (`FlatList`) ou un état centré, `edges={['top', 'left', 'right']}` dans un onglet.
3. Pour les états : `<LoadingState />`, `<ErrorState error={error} onRetry={refetch} />`, `<EmptyState title=… />`.
4. Utiliser `Text`, `Button`, `TextField`, `Card`, `ListItem`, `Avatar`, `ChoiceGroup` et les jetons de `useTheme()` ; aucune couleur ni taille en dur.
5. Ajouter un test de rendu ou de comportement dans un `__tests__/` hors de `src/app/`.

### Couleurs (charte « Uzu »)

La palette est celle du pelage d'un Jack Russell (voir l'[ADR 0030](../docs/adr/0030-charte-graphique-uzu.md)) ; les valeurs vivent dans `src/theme/tokens.ts`, en clair et en sombre :

- **fond blanc cassé** (`background`, `surface`) et **noir chaud** pour le texte (`text`) ;
- **fauve** (`primary`) pour l'action : boutons principaux, « + », pastille de filtre active, onglet actif, liens, épingles des balades ;
- **sable** pour les teintes douces : fond sélectionné (`primarySoft`), pastilles et bannières d'information (`infoSoft`), liserés (`border`) ;
- **balades des professionnels** en `pro` : noir chaud en clair, blanc cassé en sombre (badge « Pro », bordure de la fiche, épingle), toujours avec le libellé « Pro » ;
- le rouge de danger reste un rouge, brique, accordé à la palette.

Les épingles de la carte sont dessinées par l'application (`WalkPin` dans `WalksMap.tsx`) : un rond avec une patte, plus grand quand la balade est sélectionnée ; les épingles natives n'acceptent pas de glyphe. Le verre non teinté porte une teinte légère du fond (`glassTint`), et le panneau un voile du fond (`PANEL_VEIL`), pour pencher vers le blanc cassé plutôt que vers le vert d'Apple Plans, qu'on ne peut pas recolorer. Les paires texte/fond sont vérifiées dans `src/theme/__tests__/theme.test.ts` (4,5:1 pour le texte, 3:1 pour les icônes porteuses de sens) : une nouvelle paire utilisée s'y ajoute.

### Logo, icônes et écran de démarrage

Le logo est la tête de la Jack Russell du porteur du produit, dessinée en aplats d'après une photo (voir l'[ADR 0044](../docs/adr/0044-logo-et-icone-uzu.md)). Sa source vectorielle est `assets/brand/uzu-head.svg` ; toutes les images en sont tirées par `npm run brand:export` (`scripts/export-brand-assets.mjs`, rendu par `@resvg/resvg-js`), qui les écrit dans `assets/` :

| Fichier                       | Contenu                                                                                 |
| ----------------------------- | --------------------------------------------------------------------------------------- |
| `icon.png`                    | Icône iOS : 1024 px, fond sable plein, **sans canal alpha** (exigence de l'App Store)   |
| `android-icon-foreground.png` | Avant-plan de l'icône adaptative, transparent, tête dans le cercle sûr de 66 dp sur 108 |
| `android-icon-monochrome.png` | Icône à thème d'Android 13 : silhouette blanche, yeux, truffe et bouche découpés        |
| `splash-icon.png`             | Écran de démarrage : disque sable avec la tête, lisible sur le fond clair et le sombre  |
| `favicon.png`                 | 48 px (le web n'est pas une plateforme livrée, la configuration le demande quand même)  |

Le fond de l'icône adaptative est la couleur `BRAND.iconBackground` (sable, égal au jeton `infoSoft`). Le script refuse d'écrire une image qui sort des règles : pixel transparent dans l'icône iOS, dessin hors du cercle visible sur Android (avant-plan, monochrome, et les deux tiers centraux de l'image de démarrage, seuls montrés par Android 12+). Pour modifier le logo : éditer le SVG (garder les identifiants `ears`, `head`, `markings`, `features`, utilisés pour la silhouette), lancer `npm run brand:export`, puis `npm test` (`appConfig.test.ts` vérifie la taille et la transparence de chaque image) ; versionner le SVG et les PNG ensemble. `notification-icon.png` n'en fait pas partie : silhouette blanche sur fond transparent, teintée par Android de la couleur principale.

### Ajouter un texte

Tout texte affiché va dans `src/strings/fr.ts`, rangé par écran ou fonctionnalité, puis s'utilise par `t.section.cle`. Les variables s'écrivent `{nom}` et se remplissent avec `format(t.profile.greeting, { name })`. Les énumérations s'affichent avec `enumLabel('DogSize', value)`. Un nouveau code d'erreur de l'API s'ajoute dans `t.errors` sous son nom exact (`walk.full`).

### Ajouter un appel à l'API

```ts
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';

export function useMyDogs() {
  return useQuery({
    queryKey: ['me', 'dogs'],
    queryFn: () => unwrap(api.GET('/api/v1/me/dogs')),
  });
}

export function useCreateDog() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateDogRequest) => unwrap(api.POST('/api/v1/dogs', { body })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['me', 'dogs'] }),
  });
}
```

- Toujours passer par `api` (jeton, renouvellement et rejeu sont automatiques) et `unwrap()` (échec → `ApiError` avec un message français dans `error.message`, le code dans `error.code`, les erreurs de champ dans `error.fieldErrors`).
- Les types viennent de `schema.d.ts` (`components['schemas']['…']`) ; ne pas recopier de modèle à la main.
- Les clés de requête commencent par la ressource (`['me', …]`, `['walks', id]`).
- Une mutation **ne doit pas attendre** le rechargement des listes qu'elle rend périmées : `onSuccess` appelle `walksChanged()`, `organizationsChanged()` ou `invalidateDogs()`, qui lancent les invalidations sans les attendre (sinon, sur un réseau lent, le bouton tourne alors que l'écriture a réussi). Ce qui change le contenu d'une balade (profil, chien, organisation) passe par ces mêmes fonctions : les balades et les pages d'organisation se mettent à jour.
- Les photos (`photoUrl`, chemins relatifs) s'affichent avec `Avatar` ou `photoUri(photoUrl)`.

### Balades (lot 2)

- **Onglet « Balades »** (`(tabs)/index.tsx`, deux vues, voir plus bas) : carte (`WalksMap`) et liste (`WalkCard`) des mêmes balades, filtres période et taille. Quand la recherche atteint son plafond (100 résultats, `isSearchCapped()`), l'en-tête dit que la liste est partielle (« 100 balades affichées : zoome pour affiner ») au lieu d'annoncer 100 balades comme un total, et après une erreur il dit « Impossible de charger les balades », jamais « Aucune balade ». La recherche suit la zone visible : `searchParamsFor(region, filtres)` (`src/api/walks.ts`) calcule centre, rayon (plafonné à 100 km) et fin de période ; la requête part 400 ms après le dernier déplacement et garde les résultats précédents pendant le chargement (indicateur « Recherche… » après 300 ms seulement).
- **Fiche** `walks/[id]` : tout sur la balade, inscription de mes chiens (`WalkRegistrationPanel`), et la section « Participants » en une seule carte (`ParticipantsCard`) : jauge de places, pour l'organisateur les demandes à valider (boutons ronds ✓ et ✕), puis une ligne par chien (`ParticipantRow` : photo du chien avec la petite photo de son maître, « maître · gabarit », « Toi » sur son propre chien ; toucher la ligne ouvre le chien). L'organisateur retire un chien (avec confirmation) par le menu « … » de la ligne (`ActionMenu` : voir le chien, voir le maître, retirer), en glissant la ligne vers la gauche (`ReanimatedSwipeable`) ou par l'action « Retirer » des lecteurs d'écran. La fiche permet aussi la modification et l'annulation.
- **Repère du rendez-vous** : une nouvelle balade n'est validée que si la personne a touché la carte ou déplacé le repère (même quand la position est connue : on ne publie pas l'adresse de l'organisateur par défaut) ; sans permission de position, un avertissement le dit, avec « Ouvrir les réglages ». À la modification, le point enregistré compte comme placé (`pinPlaced`).
- **Formulaire** `walks/new`, `walks/[id]/edit` : `WalkForm` (validation `validateWalk` dans `src/lib/forms.ts`, point de rendez-vous sur `MeetingPointPicker`). Les conflits `walk.capacity_below_registered` et `walk.size_in_use` s'affichent sur leur champ (`walkSubmitErrors`).
- **« Mes balades »** : `GET /me/walks` avec `role` (`Organizer`, `Participant`) et `when` (`Upcoming`, `Past`), en PascalCase.
- **Cohérence** : toute écriture sur une balade passe par `walksChanged()`, qui met la fiche à jour depuis la réponse et invalide `['walks']`, `['me', 'walks']` et `['organizations']` (la page d'une organisation liste ses balades à venir), sans attendre les rechargements.
- **Dates** : `src/lib/dates.ts` (`formatWalkStart` → « Demain à 09:30 », `formatDuration` → « 1 h 30 »), en français et dans le fuseau de l'appareil (choix de produit à confirmer : l'heure d'une balade s'affiche dans le fuseau de celui qui la regarde, pas dans celui du lieu). Pluriels : `plural(t.discover.resultCount, n)`.
- **Champs des lots suivants** (`isProfessional`, `organization`, `hasBlockedParticipant`) sont déjà affichés par `WalkCard` et la fiche (badge « Pro » et bordure dans la couleur `pro`, nom de l'organisation, avertissement discret).

### Onglet « Balades » : deux vues au choix

L’onglet a deux vues, au choix de chacun dans le profil, sous **Réglages › Affichage des balades** (deux cartes avec une petite illustration : « Carte et panneau » et « Liste »). **« Carte et panneau » est la vue par défaut.** Le choix s’applique aussitôt à l’onglet et reste mémorisé sur l’appareil (`expo-secure-store`, clé `uzu.discoverLayout`) ; une valeur inconnue (ou l’ancienne « current ») redonne le panneau. Il ne dépend pas de `EXPO_PUBLIC_DEV_LOGIN`.

- Le code vit dans `src/discover/` : `shared.tsx` (état des filtres partagé par les deux vues, en-tête de la liste et rangée de pastilles, contrôles flottants sur la carte, recherche qui suit la carte, actualisation, boutons flottants), `PanelLayout.tsx`, `ListLayout.tsx`, `layoutPreference.ts` (la préférence) et `LayoutSetting.tsx` (le réglage du profil). La route `(tabs)/index.tsx` choisit la vue et garde les filtres.
- **Préférence observable** : `layoutPreference.ts` est un petit store (`useSyncExternalStore`) lu une fois depuis le stockage sécurisé, puis partagé par l’onglet et le réglage. `useDiscoverLayout()` renvoie `{ layout, setLayout }` ; `layout` vaut `null` le temps de la lecture (quelques millisecondes), l’onglet n’affiche alors rien plutôt que la mauvaise vue.
- **Filtres, communs aux deux vues** : une rangée de pastilles-menus sous le titre et « Créer » (`FilterBar`, composant `MenuPill`) : période (« 7 jours », par défaut ; Aujourd’hui, 7 jours, Toutes les dates), taille (« Taille », puis la valeur choisie : Petit, Moyen, Grand) et, **seulement dans la liste**, distance (« 10 km » ; 5, 10, 25 km : la carte plein écran suit sa zone visible). Chaque pastille est une capsule du matériau flottant (`Material`) ; une pastille dont la valeur diffère de la valeur par défaut est teintée de la couleur principale, en gras, et affiche la valeur choisie (pas seulement une couleur). Toucher une pastille ouvre son menu **sous elle** : sur iOS, le vrai menu du système (SwiftUI `Menu`, `@expo/ui`, inclus dans Expo Go, voir `src/components/nativeMenu.ts`) ; ailleurs (Android, web, tests), le menu de l’application (`AnchoredMenu`, partagé avec `ActionMenu`, le menu « … » des lignes de participants) : il grandit depuis la pastille avec un ressort court, sur un matériau translucide, coche sur le choix courant, séparateurs fins, sans voile gris (un fond transparent capte le toucher extérieur pour fermer), recalé pour ne jamais sortir de l’écran (`placeMenu`), fermé aussi par le bouton retour d’Android. Un choix s’applique tout de suite et ferme le menu. Accessibilité : la pastille est un bouton « Taille, Petit » (état déplié), le menu nomme ses éléments et coche le choix. Les filtres vivent dans l’écran de l’onglet (`useDiscoverFilterState`) : ils restent les mêmes quand on change de vue dans le profil, pour toute la session.
- **Bouton « Ma position »** : capsule ronde du matériau flottant, de la taille des pastilles, en bas à droite de la carte (panneau et carte plein écran de la vue Liste), qui recentre la carte sur la position de l’utilisateur ; dans le panneau, il suit en continu le bord haut du panneau pendant le glissement (`animatedPosition` de `@gorhom/bottom-sheet`), sans monter au-dessus des pastilles, et s’efface à la position haute (le verre se « dématérialise » au lieu de baisser son opacité, que le verre ne supporte pas). Il n’est pas affiché quand la position est refusée ou indisponible (une phrase l’explique déjà sous les filtres).
- **« Carte et panneau »** : la carte occupe tout l’écran, jusqu’en haut, sans bande : seules des capsules flottent dessus (`MapTopControls`) : les pastilles à gauche et « Créer » à droite (bouton rond « + », verre teinté de la couleur principale, libellé « Créer une balade »). Le titre « Balades » n’est plus affiché (l’onglet le dit) mais reste un en-tête pour les lecteurs d’écran. Le panneau s’arrête, en position haute, sous les pastilles (`topInset`). **Marge de la carte** : le bas de `mapPadding` suit la position de repos du panneau (basse ou moyenne ; la haute garde celle du milieu, le logo est alors couvert), si bien que le logo « Plans » et « Légal » restent juste au-dessus du panneau ; elle suit la position visée (`onAnimate`), pas chaque image d’un glissement. Sur iOS, cette marge ne déplace que ces éléments (vérifié : la carte ne bouge pas, aucune recherche n’est relancée) ; la zone cherchée est toute la carte, panneau compris, donc « N balades dans cette zone » ne change pas quand on bouge le panneau. Sur Android, Google Maps recentrerait la caméra à chaque changement : la marge y reste celle de la position moyenne. Le panneau est sur le même matériau que les capsules aux positions basse et moyenne, et devient opaque en montant vers la position haute (comme les feuilles d’iOS), les fiches restant sur leur fond. Il a trois hauteurs (`@gorhom/bottom-sheet`) ; son en-tête tient sur une ligne : « N balades dans cette zone » à gauche (la liste suit la zone visible de la carte), un chevron et le bouton « Actualiser les balades » à droite. L’en-tête est un bouton : « Voir la liste » → « Agrandir la liste » → « Voir la carte ». Une épingle touchée remonte la balade en tête du panneau, mise en évidence. Pas de « tirer pour rafraîchir » (le geste servirait à baisser le panneau).
- **« Liste »** : liste plein écran, qui défile sous la barre d’état (un matériau translucide y apparaît dès que la liste défile), mêmes pastilles plus la distance (10 km par défaut), recherche autour de la position (ou du centre par défaut, avec une phrase), triée par date (`searchParamsAround`). « Créer » y est le même bouton rond, à côté du titre. Le bouton « Carte » (capsule teintée) ouvre une carte plein écran où les mêmes pastilles flottent, sans bande (sans la distance, recherche sur la zone visible) ; une épingle touchée affiche la balade en bas (« Fermer », ou toucher la balade pour l’ouvrir) ; « Liste » revient, filtres, défilement et dernière zone de carte conservés. Tirer pour rafraîchir y est conservé.
- **Actualisation au retour sur l’onglet** : les deux vues relancent la recherche quand l’onglet reprend le focus (`useRefetchOnFocusIfStale`, `src/lib/hooks.ts`), mais pas au premier affichage ni si les données ont moins de 30 secondes.
- **Mise en place** : `react-native-gesture-handler`, `react-native-reanimated` et `react-native-worklets` (versions d’Expo SDK 58, `npx expo install`) et `@gorhom/bottom-sheet` ; `GestureHandlerRootView` enveloppe l’application (`AppProviders`). Le plugin Babel des worklets est ajouté par `babel-preset-expo`, sans configuration. Dans les tests, `jest.setup.ts` charge les simulations de gesture-handler et remplace le panneau par une vue dont `snapToIndex` appelle `onChange` ; les tests qui changent de vue appellent `resetDiscoverLayout()` pour oublier le choix gardé en mémoire.
- **Finition native** : les jetons de mouvement, d’ombre et de matériau sont dans le thème (`motion`, `shadows`, `material`, couleurs `materialTint`, `materialFallback`, `glassTint`, `glassEdge`, `separator`, hauteur `controlHeight` des capsules, 44 pt dans une zone de toucher de 48). `PressableScale` réduit légèrement un contrôle pressé avec un ressort (pastilles, boutons, fiches de balade, boutons flottants) ; `haptics` (`src/lib/haptics.ts`, `expo-haptics`) donne un retour léger au choix dans un menu, au changement de hauteur du panneau et à l’épingle touchée ; `Material` est la seule surface de la couche flottante (pastilles, boutons, fiches et messages sur la carte, panneau, menus, barre d’état de la liste) : sur iOS 26 et plus, le verre liquide du système (`GlassView` d’`expo-glass-effect`, inclus dans Expo Go, après `isLiquidGlassAvailable()` et `isGlassEffectAPIAvailable()`), teinté de la couleur principale pour l’action principale et les pastilles actives (`tinted`) ; sur un iOS plus ancien, le flou d’`expo-blur` sous une teinte légère, avec un liseré clair et une ombre douce ; sur Android et le web, un fond du thème presque opaque. Les barres (`variant="bar"`) restent sur le flou, comme celles d’iOS. Avec « Réduire la transparence » (iOS), toutes les surfaces deviennent opaques. Pour faire disparaître une capsule de verre, on change son style (`hidden`), jamais son opacité ni celle d’un parent. Avec « Réduire les animations », les contrôles ne rétrécissent plus, le menu apparaît en fondu et le panneau suit le réglage du système. Dans les tests, Reanimated tourne sur son implémentation JavaScript (`resolver` de `jest.config.js`), le menu natif est remplacé par celui de l’application, `expo-haptics`, `expo-blur` et `expo-glass-effect` sont simulés (`jest.setup.ts` ; le verre y est indisponible, sauf dans `Material.test.tsx`), et la carte simulée expose sa `mapPadding`.

### Fil de balade, répétition et notifications (lot 3)

- **Fil** `walks/[id]/messages` : visible de l'organisateur et des propriétaires de chiens acceptés (`canUseThread`, `src/lib/thread.ts`) ; les autres voient, sur la fiche, une phrase qui explique l'absence du bouton. `useWalkMessages` (`src/api/messages.ts`) charge la dernière page (clé `['walks', id, 'messages']`, donc rafraîchie par `walksChanged`), la recharge au focus, par tirer pour rafraîchir et toutes les 15 s tant que l'écran est au premier plan, et ajoute les pages plus anciennes avec `before=<id du premier message>`. **Le cache du fil est cumulatif** : chaque lecture est fusionnée (sans doublon, par date) à ce qu'il contient au moment de la réponse, si bien qu'un message déjà reçu ne disparaît jamais ; si plus d'une page est arrivée entre deux lectures, les pages intermédiaires sont relues. L'envoi annule les lectures en cours avant d'écrire dans le cache. `useSendMessage` envoie de façon optimiste : le texte s'affiche aussitôt (« Envoi… »), puis devient le vrai message, ou « Message non envoyé » avec « Réessayer » / « Supprimer ». Annulée : lecture seule avec une bannière.
- **Notifications** (`src/notifications/`, voir l'[ADR 0022](../docs/adr/0022-notifications-push-mobile.md)) : `expo-notifications` n'est chargé que si `EXPO_PUBLIC_EAS_PROJECT_ID` est défini ; sinon tout est inactif, sans message. `offerNotifications()` explique puis demande l'autorisation (une seule fois, après une inscription ou une création de balade) ; `registerDevice()` / `unregisterDevice()` gèrent `PUT` et `DELETE /me/devices` (la déconnexion supprime l'appareil avant d'effacer la session) ; `targetForNotification()` ne suit que `uzu://walks/<id>` (le fil pour `message_posted`, sinon la fiche) ; `NotificationsEffects` (dans la disposition racine) écoute les notifications et le renouvellement du jeton push (`addPushTokenListener` : l'appareil est ré-enregistré, l'ancien jeton retiré). Un tap reçu pendant que la session se lit attend la connexion ; dès que la personne est déconnectée, un tap mémorisé est oublié et ceux qui arrivent sont ignorés (ils peuvent venir d'un autre compte). Les tests activent la fonction avec `config.easProjectId = '…'` ; `expo-notifications` y est simulé (`jest.setup.ts` : `__tap(data)`, `__receive(data)`, `__state`).
- **Notifications reçues, sur un appareil** : il faut une version de développement et un projet EAS (`eas init`, puis `EXPO_PUBLIC_EAS_PROJECT_ID`) ; ni Expo Go ni le simulateur sans projet ne peuvent obtenir de jeton. Sur Android, Expo Go ne gère plus les notifications push depuis le SDK 53.
- **Réglages** : écran `notifications` (onglet « Profil »), quatre interrupteurs liés à `/me/notification-preferences` (`useUpdateNotificationPreferences(change)`, enregistrement immédiat, retour en arrière en cas d'échec ; les envois se font l'un après l'autre, chacun portant les valeurs du cache à son tour) et l'état de l'autorisation du téléphone.
- **Répétition** : `repeatWeekly` dans `WalkForm` (création seulement, option `allowRepeat`), badge « Chaque semaine » (`repeatsWeekly`) sur `WalkCard` et la fiche, « Arrêter la répétition » (`useStopSeries`) pour l'organisateur.
- `SwitchRow` (interrupteur avec description) est le composant à réutiliser pour tout réglage.

### Organisations, blocage, signalement, compte (lot 4)

- **Ordre des écrans.** Les `Stack.Screen` / `Tabs.Screen` d'une disposition se déclarent dans l'ordre de tri d'Expo Router (`index` et groupes, routes statiques par longueur de nom, routes dynamiques) ; sinon le lancement affiche en développement une erreur « ROUTE_NAMES_CHANGED … not handled by any navigator ». Un test (`navigation-order.test.ts`) le vérifie ; voir l'[ADR 0026](../docs/adr/0026-ordre-de-declaration-des-ecrans.md). L'onglet « Mes balades » est donc `mine`.
- **Organisations** : onglet « Profil » (mes organisations, « Trouver », « Créer »), `organizations/` (recherche à 400 ms, création, fiche, modification), `organizations/[id]/members` (responsable et éducateurs ; changement de rôle réservé au responsable). Hooks dans `src/api/organizations.ts` ; toute écriture appelle `organizationsChanged()` (liste, fiche, membres). `organizingOrganizations()` donne les organisations pour lesquelles je peux organiser (approuvée, adhésion acceptée, responsable ou éducateur) : `WalkForm` montre alors « Organiser pour » (création seulement, `organizationId`).
- **Balade professionnelle** : le nom de l'organisation ouvre sa page ; un refus `walk.members_only` à l'inscription affiche une carte avec « Voir l'organisation » (`WalkRegistrationPanel`).
- **Organisateur supprimé** : `organizer` peut être `null` (« Compte supprimé », sans lien) dans `WalkCard` et la fiche.
- **Chiens partagés et invitations** (ADR 0042) : un chien a plusieurs propriétaires (`members`, `isDogOwner()` et les phrases « avec … » dans `src/lib/dogMembers.ts`). Sur `dogs/[id]`, un propriétaire voit « Ajouter un humain » (feuille modale `dogs/[id]/invite` : choix du rôle, seul « Propriétaire » est actif, puis création de l'invitation et feuille de partage native avec `uzu://invitation/<jeton>` ; le lien est créé une fois, repartager le réutilise), les invitations en attente (« Annuler l'invitation ») et, selon le nombre de propriétaires, « Supprimer » (dernier propriétaire) ou « Ne plus m'occuper de … ». L'écran `invitation/[token]` (lien profond `uzu://invitation/<jeton>`) montre qui invite, le rôle et l'échéance, puis « Accepter » ou « Plus tard » ; il explique les refus (`invitation.not_found`, `invitation.already_used`, `invitation.expired`, `dog.already_owner`, `dog.limit_reached`). Hooks dans `src/api/dogs.ts`. Le lien marche aussi pour une personne qui n'est pas encore connectée (voir « Lien d'invitation avant la connexion » ci-dessous). Dans `WalkRegistrationPanel`, un chien déjà inscrit par un autre propriétaire (`myDogsRegistrations`) n'est pas sélectionnable (« Déjà inscrit par … ») et son inscription, annulable par tout propriétaire, apparaît dans « Mes inscriptions » avec « par … ». Tester un lien en local, connecté : `xcrun simctl openurl booted "uzu://invitation/<jeton>"` (Android : `adb shell am start -a android.intent.action.VIEW -d "uzu://invitation/<jeton>"`) ; le jeton est dans le champ `token` de la réponse de `POST /api/v1/dogs/{id}/invitations` (voir `api/README.md`), ou dans la fin du lien partagé depuis l'application.
- **Blocage** : « Bloquer » / « Débloquer » sur `users/[id]` (confirmation qui explique l'effet), liste `blocked`, et `src/api/blocks.ts` qui rafraîchit aussi les balades (`walksChanged`) pour le badge « Participant bloqué » (carte et fiche, avec une phrase d'explication).
- **Signalement** : un seul écran `report?kind=user|dog|walk&id=…&name=…` (`reportHref()` dans `src/lib/report.ts`), ouvert depuis un profil, un chien et une balade qui ne sont pas les miens.
- **Réglages et compte** : l'onglet « Profil » range « Mes chiens », les organisations, « Réglages » (notifications, personnes bloquées, textes légaux, suppression) puis « Se déconnecter ». `delete-account` demande de taper `SUPPRIMER`, appelle `DELETE /me`, puis `endSession()` (session effacée sur l'appareil seulement : ni déconnexion ni retrait de l'appareil, le serveur s'en est chargé) et l'écran de connexion affiche une phrase (`setSignInNotice`). Un compte `Suspended` voit un bandeau dans le profil.
- **Textes légaux** : `src/strings/legal.ts`, affichés par `LegalDocument` dans `(legal)/terms` et `(legal)/privacy` (accessibles connecté ou non, ADR 0025), avec le bandeau « Version provisoire — à faire valider avant la publication » et des passages `[…]` à compléter (éditeur, adresse, contact, droit applicable, durée de conservation des signalements).
- **Notifications** : `registration_cancelled_by_owner` (un autre propriétaire a annulé l'inscription de mon chien) ouvre la fiche de la balade ; `membership_requested` ouvre l'écran des membres, `membership_accepted` et `organization_approved` la fiche de l'organisation ; seul `uzu://organizations/<id>` est suivi, et le type doit correspondre au genre de lien.
- **Actualisation au retour** : `useRefetchOnFocus()` (`src/lib/hooks.ts`) relit le profil, les organisations et les membres quand l'écran reprend le focus (par exemple après une validation par l'équipe).
- **Administration** : pas d'écran (ADR 0020), à faire avec `curl` (voir `api/README.md`, « Agir en administrateur en local »).

### Session

- `useSession()` donne `status` (`loading`, `signedOut`, `signedIn`), `user`, `setSession(réponse)`, `updateUser(user)` (après une modification du profil : met à jour la session et son stockage) et `signOut()`.
- Après un appel de connexion (`/auth/apple`, `/auth/google`, `/auth/dev`), appeler `setSession(réponse)` : la garde affiche alors les onglets.
- La session est conservée dans `expo-secure-store`. Avant d'être effacée à la déconnexion, `onBeforeSignOut` (3 s au plus, échec ignoré) retire l'appareil des notifications. Le cache des données est vidé à la déconnexion. Voir l'[ADR 0033](../docs/adr/0033-fin-de-session-et-delais-reseau.md) pour ce qui suit.
- **Renouvellement** (`src/api/authFetch.ts`) : un seul en vol, partagé par les requêtes ; la session n'est fermée que si `/auth/refresh` répond 401 (jeton invalide ou révoqué) ou 403 `account.suspended`, ou si la requête rejouée avec le nouveau jeton répond encore 401. Toute autre réponse (réseau, délai, 5xx, 429, 404, 408, 413…) est passagère : la session est gardée et l'erreur affichée est la vraie (« Trop de demandes… », « Impossible de joindre le serveur… »).
- **Fin de session expliquée** : une fin imposée porte une raison (`endReason` : `expired` ou `suspended`) ; `SessionProvider` la traduit en phrase sur l'écran de connexion (`t.auth.sessionExpired`, `t.auth.accountSuspended`) par `setSignInNotice`, comme après une suppression de compte. Une déconnexion volontaire n'affiche rien.
- **Persistance** (`src/auth/sessionStore.ts`) : l'état en mémoire est publié tout de suite, puis les opérations du stockage s'appliquent une à une dans l'ordre des appels (une déconnexion pendant l'écriture d'un renouvellement ne ramène jamais la session). Une écriture échouée est réessayée (3 essais) ; si elle échoue encore, l'entrée stockée est effacée pour ne jamais présenter un ancien jeton au lancement suivant (ce qui révoquerait toutes les sessions, ADR 0009), la session restant en mémoire ; un journal le dit en développement. À la déconnexion, la révocation part toujours, même si l'effacement local échoue, et l'état passe à « déconnecté ».
- **Délais** : 20 s par requête (`DEFAULT_TIMEOUT_MS`), 60 s pour l'envoi d'une photo (`UPLOAD_TIMEOUT_MS`, `PUT …/photo`), y compris `/auth/refresh` et `/auth/logout`, jusqu'à la réception des en-têtes et combinés avec l'annulation de TanStack Query. Un délai dépassé donne une `ApiError` réseau avec `timedOut` et le message `t.errors.timeout` ; une requête expirée n'est jamais réessayée (elle a déjà attendu 20 s), un 429 non plus. Si le renouvellement de session échoue alors que le jeton a déjà expiré, la requête n'est pas envoyée : l'échec du renouvellement est la réponse (sinon, deux délais de 20 s se suivaient contre une API injoignable).

### Permissions

Les textes iOS et Android (position, photos, appareil photo) sont dans `app.config.ts`. Le module qui en a besoin (`expo-location`, `expo-image-picker`…) doit reprendre les mêmes textes dans les options de son plugin. Le texte d'explication avant la demande d'autorisation des notifications est `t.permissions.notifications`.

**Permission refusée.** Là où un refus est signalé (onglet Balades, création de balade, sélecteur de photo pour l'appareil photo, écran des notifications), un bouton « Ouvrir les réglages » mène aux réglages du téléphone (`openAppSettings()`, `src/lib/linking.ts`, qui ne lève jamais d'erreur). `useUserLocation()` relit la permission (sans la redemander) chaque fois que l'application revient au premier plan tant que la position manque. Pour ouvrir un plan, `openMapsAt()` essaie l'adresse native puis une adresse `https` de Google Maps, et la fiche prévient si rien ne s'ouvre.

### Photos, formulaires et tests des écrans

- Choisir une photo : `PhotoPicker` (photothèque ou appareil photo, via `pickPhoto()` de `src/lib/photoPicker.ts`, qui recadre en carré, réduit à 1280 px et réencode en JPEG). L'envoi se fait par `useUploadMyPhoto()` / `useUploadDogPhoto()` (multipart, champ `file`). La partie fichier est un `File` d'`expo-file-system` : depuis le SDK 58, le `fetch` global est celui d'`expo/fetch`, qui refuse l'objet `{ uri, name, type }` propre à React Native (voir `src/api/photoUpload.ts`). Une erreur levée dans l'application (et non par le réseau) donne une `ApiError` de type `client`, jamais « Impossible de joindre le serveur ». Les erreurs `photo.too_large` et `photo.unsupported_type` ont leur message français.
- **Navigation après un `await`** (enregistrement, création, suppression) : `useFocusedRouter()` (`src/lib/hooks.ts`) donne `back()` et `replace()` qui ne font rien si l'écran n'a plus le focus ; sans cela, un retour arrière pendant l'envoi faisait dépiler un écran de trop. À utiliser à la place du routeur dans tout écran qui navigue après une écriture.
- **Lien d'invitation avant la connexion** (`src/app/+native-intent.ts`, `src/lib/pendingInvitation.ts`) : l'écran `invitation/[token]` est derrière la garde d'authentification, qui renverrait une personne déconnectée vers `/sign-in` en oubliant le lien. Expo Router passe chaque lien reçu par `redirectSystemPath` de `+native-intent` ([documentation](https://docs.expo.dev/router/advanced/native-intent/)) avant de naviguer, au démarrage à froid (`initial: true`) comme application ouverte : un lien d'invitation y est mis de côté en mémoire comme **lien en attente** (le routeur ne reçoit rien, l'application reste où elle est ou démarre sur son écran habituel). La disposition `(app)/_layout.tsx`, qui n'existe que connecté, l'ouvre dès que la personne est connue, **une seule fois** (le lien est retiré au moment de l'ouvrir : « Plus tard » ne le ramène pas). Pour un nouveau compte, l'invitation passe après le profil (`edit-profile?welcome=1`) et **avant** « Ajoute ton premier chien », puisqu'accepter donne un chien : l'écran du profil la prend en charge à la sauvegarde comme à « Plus tard », et, s'il est fermé autrement, la disposition l'ouvre quand l'accueil est terminé (`welcomeNudge`). Le lien en attente reste en mémoire seulement (jamais écrit sur l'appareil), n'est ni affiché ni journalisé (le jeton est un secret) et est effacé quand une session connectée se termine (`AppProviders`, `onSignedOut`). Pas de saisie manuelle du lien ni de texte « tu as été invité » sur l'écran de connexion. Tester : se déconnecter, puis `xcrun simctl openurl booted "uzu://invitation/<jeton>"` (Android : `adb shell am start -a android.intent.action.VIEW -d "uzu://invitation/<jeton>"`) ; l'écran de connexion reste affiché ; se connecter (un compte déjà connu arrive sur l'invitation ; un compte neuf passe par son profil, puis arrive sur l'invitation). À essayer aussi application fermée (démarrage à froid) et application ouverte (arrière-plan). Tests : `src/__tests__/pending-invitation.test.tsx`.
- **Navigation par appui** : `useGuardedRouter()` (`src/lib/navigation.ts`) remplace `useRouter()` (une règle ESLint l'impose) : `push`, `navigate` et `replace` ignorent la même destination (chemin et paramètres) tant que la navigation réclamée n'a pas eu lieu puis n'a pas été quittée : le garde tombe au deuxième changement de chemin (arrivée sur l'écran, puis retour ou départ ailleurs), observé par `useNavigationGuardReset` (monté dans `src/app/_layout.tsx`). Des appuis espacés d'une seconde sur un appareil lent n'empilent donc plus l'écran, et revenir puis rouvrir la même destination marche tout de suite. Si le chemin ne change pas du tout (navigation qui n'aboutit pas), le garde tombe au bout de 5 s (`PENDING_NAVIGATION_TIMEOUT_MS`). Une autre destination n'est jamais retenue. Voir l'[ADR 0040](../docs/adr/0040-navigation-gardee-et-rafraichissement-apres-refus.md).
- **Refus 404 et 409 d'une écriture** : le `MutationCache` (`src/api/queryClient.ts`) invalide alors toutes les requêtes, pour que l'écran montre l'état réel derrière le message (balade pleine, organisation plus validée, demande déjà traitée, adhésion retirée). Rien à faire dans les écrans. Un message d'erreur 4xx définitif (`ErrorState`) propose « Retour » au lieu de « Réessayer » ; un 429 annonce le délai du `Retry-After`. Un 404 a un titre qui dit ce qui manque au lieu de « Oups, un problème est survenu » : `notFoundTitle` (« Balade introuvable », « Chien introuvable », « Organisation introuvable », « Profil introuvable »), « Introuvable » à défaut.
- **Codes d'erreur** : `src/strings/__tests__/strings.test.ts` lit `api/src/Dogo.Api/Common/ErrorCodes.cs` et exige un message français pour chaque code (hors `error`, `not_found`, `method_not_allowed`, `server.error`, couverts par le statut) et aucun message pour un code disparu. Un refus qui vise un champ (`dog.size_in_use` → la taille) se déclare dans `fieldOfCode` (`src/lib/forms.ts`).
- **Règles de l'API reprises côté formulaire** : titre de balade de 3 à 80 caractères sans les espaces de bord ; début au plus un an plus tard (`latestWalkStart()`, borne du sélecteur et validation) ; 20 chiens au plus (`limits.dogsMax` : le profil remplace « Ajouter un chien » par une explication, `dogs/new` n'ouvre pas le formulaire) ; tout instant envoyé passe par `toApiDateTime()` (UTC avec `Z`) ; énumérations envoyées par leur nom ; liste des membres demandée avec `limit=100`, avec la mention « liste partielle » quand elle est pleine. `src/api/__tests__/wire-format.test.ts` vérifie le format réel sur le fil.
- **Compte suspendu** : `DELETE /me` est permis (ADR 0036) ; l'écran `delete-account` et son entrée du profil ne dépendent pas du statut. Les jetons d'un compte suspendu sont révoqués : l'écran n'est atteignable que tant que la session ouverte tient encore. La notification `report_created` (administrateurs) est connue et ignorée (ni lien ni navigation).
- **Taille du texte** : `useLargeText()`, `useLineLimit(n)`, `useFontScale()` et `scaledSize()` (`src/theme/textScale.ts`) : à partir de 1,3 (Android annonce son « 130 % » comme 1,2999999 : le seuil a une tolérance de 0,001), les limites de lignes tombent, les rangées de boutons s'empilent, les pastilles de filtres de la liste passent à la ligne au lieu de défiler de côté (`FilterBar wrap`), les hauteurs minimales croissent (jamais de hauteur fixe pour un champ ou un bouton). Le mot d'un bouton flottant (« Carte », « Liste ») ne grandit que jusqu'à 1,5 fois : il couvre les fiches. Dans les tests, `setFontScale(2.5)` (`src/test/fontScale.ts`) règle le facteur ; il vaut 1 au début de chaque test.
- **Données anciennes** : si le rafraîchissement de l'onglet Balades échoue alors que d'anciennes balades sont affichées, `StaleNotice` le dit discrètement (panneau et liste) ; la recherche réessaie alors seule toutes les 30 s (`staleSearchRetryInterval`, `src/api/walks.ts`), et le signal disparaît dès que l'API répond.
- **Android** : `expo-system-ui` est installé (sans lui `userInterfaceStyle: 'automatic'` est ignoré sur Android) ; `Screen` utilise `KeyboardAvoidingView behavior="padding"` sur les deux plateformes (affichage bord à bord), sans `softwareKeyboardLayoutMode: 'pan'`. Le décalage vertical vient de `measureScreenTop` : `measure` (`pageY`) sur Android, où `measureInWindow` ne compte pas la barre d'état (le champ du fil restait à moitié sous le clavier), `measureInWindow` sur iOS. Le fil relance son défilement vers le dernier message à l'image suivante (sur Android, la vue native ne connaît pas encore la nouvelle hauteur au premier appel). Les cartes reçoivent `userInterfaceStyle` selon le thème (Google Maps restait clair en mode sombre ; sur Android, il n'est lu qu'à la création de la carte). Vérifié sur l'émulateur Android (API 37, Expo Go) : champ au-dessus du clavier, message visible après l'envoi, carte sombre.
- **Premier chien** : après la première modification du profil (`?welcome=1`), un membre sans chien est conduit à `dogs/new?welcome=1` (« Dernière étape : présente ton chien ») ; sans chien, le profil et l'inscription à une balade proposent déjà « Ajouter un chien ».
- Formulaires : limites de l'API dans `src/lib/forms.ts` (`limits`, `validateDog`, `validateProfile`) ; les erreurs de champ du serveur s'affichent sur le bon champ par `serverFieldErrors()`.
- Défilement vers la première erreur : un formulaire rendu dans un `Screen` qui défile appelle `useScrollToFirstError()(errors)` après un échec de validation (client ou serveur). Les champs se repèrent par leur nom : prop `name` de `TextField` et `DateField`, ou `<FieldAnchor name="…">` autour d'un autre champ (groupe de puces, carte). Le composant qui appelle le crochet doit être **à l'intérieur** du `Screen`.
- Tests d'écran : `renderRouter('./src/app', …)` avec `mockApi({ 'GET /api/v1/me': () => ok(…) })` (`src/test/mockApi.ts`, qui remplace `fetch` ; `call.form` donne le corps multipart relu) ; les modules natifs (`expo-apple-authentication`, `photoPicker`, sélecteur de date) sont simulés avec `jest.mock`. `jest.setup.ts` simule pour tous les tests `expo-file-system` (`File` est un vrai `Blob`), `react-native-maps` (une vue qui transmet `regionChangeComplete` et `press`), `ReanimatedSwipeable` (une vue qui montre la ligne puis son action de glissement, que l'on touche par son `testID`) et `expo-location` (autorisée, à Altkirch ; le centre par défaut est Seppois-le-Haut). Données de balade : `walkResult()`, `walkDetail()`, `participant()` dans `src/test/fixtures.ts`.
