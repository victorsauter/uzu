# Uzu

Application mobile pour trouver et organiser des balades entre chiens, en sachant à l'avance quels chiens seront là.

Le dépôt, le code et l'infrastructure gardent le nom `dogo` (dossiers, projets .NET, base et conteneurs Docker) ; le produit s'appelle Uzu ([ADR 0041](docs/adr/0041-nom-de-l-application-uzu.md)).

## État

La V1 est développée : une API (`api/`) et une application iOS et Android (`mobile/`). Elle tourne en local et a été essayée dans le simulateur iOS. Une API de démonstration peut être mise en ligne gratuitement, avec une build pour iPhone signée par un Apple ID gratuit ([guide de la démo hébergée](docs/demo-hebergee.md), [ADR 0043](docs/adr/0043-demo-hebergee.md)). L'application n'est publiée sur aucun magasin et l'API de production n'est pas hébergée ; les étapes restantes sont listées dans le [plan](docs/plan.md#ce-que-ce-plan-ne-couvre-pas).

## Prérequis

Sur un Mac :

| Outil | Version | Rôle |
| --- | --- | --- |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | récent, **lancé** | Base PostgreSQL + PostGIS, et tests de l'API |
| [SDK .NET](https://dotnet.microsoft.com/download/dotnet/10.0) | 10.0.400 ou plus récent en 10.0 (`api/global.json`) | API |
| [Node.js](https://nodejs.org/) avec npm | 22.13 ou plus récent (développé avec Node 26) | Application mobile |
| Xcode, avec un simulateur iOS | 26.4 ou plus récent | Simulateur iOS |

Vérifier :

```bash
docker info > /dev/null && echo "Docker OK"
dotnet --version
node -v
xcodebuild -version
```

Au premier lancement de Xcode, accepter la licence et installer un simulateur iOS (Xcode > Settings > Components).

## Essayer en local

Lancer la base de données et l'API (sur `http://localhost:5080`) :

```bash
cd api
docker compose up -d --wait
dotnet run --project src/Dogo.Api
```

Le premier `docker compose up` télécharge l'image PostGIS ; sur un Mac Apple silicon, elle tourne en émulation amd64, ce qui peut prendre une à deux minutes. L'API applique les migrations de la base au démarrage : rien d'autre à configurer en développement (les valeurs de dev sont dans `api/src/Dogo.Api/appsettings.Development.json`). Laisser ce terminal ouvert.

Dans un autre terminal, préparer l'application :

```bash
cd mobile
npm ci
cp .env.example .env
```

Dans `mobile/.env`, retirer le `#` devant `EXPO_PUBLIC_DEV_LOGIN=true` pour afficher la connexion de test, puis lancer :

```bash
npm start
```

Appuyer sur `i` ouvre l'application dans le simulateur iOS, avec Expo Go (installé automatiquement au premier lancement). Si une build de développement d'Uzu est déjà installée sur le simulateur, c'est elle qui s'ouvre. Si rien ne s'ouvre, démarrer le simulateur puis lancer :

```bash
xcrun simctl openurl booted "exp://127.0.0.1:8081"
```

L'écran de connexion propose alors une « Connexion de test » : chaque identifiant saisi crée un compte distinct, ce qui permet de jouer plusieurs personnes.

Pour voir l'application avec des balades, des chiens et des messages, la même carte propose des données de démonstration fictives autour de Seppois-le-Haut, à générer et à supprimer d'un geste. Elles ne sont permises qu'en développement ; voir [`mobile/README.md`](mobile/README.md#données-de-démonstration-développement) et [`api/README.md`](api/README.md#données-de-démonstration-développement).

Les organisations se valident avec un compte administrateur de test ; la marche à suivre est dans le [README de l'API](api/README.md#agir-en-administrateur-en-local).

Émulateur Android ou téléphone : l'adresse de l'API change (`10.0.2.2` pour l'émulateur, l'adresse IP du Mac pour un téléphone) ; voir [`mobile/README.md`](mobile/README.md) et les commentaires de `mobile/.env.example`.

## Vérifier

```bash
cd api && dotnet test
```

```bash
cd mobile && npm run typecheck && npm run lint && npm test
```

Les tests de l'API démarrent leur propre base PostgreSQL et un stockage MinIO dans Docker (Testcontainers) : Docker doit être lancé, et le premier passage télécharge les images.

## Modifier un point d'entrée de l'API

1. Modifier l'API, puis `dotnet build` dans `api/` : `api/openapi.json` est régénéré ([détails](api/README.md#description-openapi)).
2. **Relancer l'API** (`Ctrl+C` puis `dotnet run --project src/Dogo.Api`) : `dotnet run` ne recharge pas le code, alors que l'application mobile, elle, se recharge seule et appellerait l'ancien contrat.
3. Dans `mobile/`, régénérer le client : `npm run api:generate && npm run typecheck` ([détails](mobile/README.md#régénérer-le-client-dapi)).
4. Versionner `api/openapi.json` et `mobile/src/api/schema.d.ts` dans le même commit : la CI échoue si `openapi.json` n'est pas à jour.

## Dépannage

| Symptôme | Cause probable et solution |
| --- | --- |
| `docker compose` : « Cannot connect to the Docker daemon » | Docker Desktop n'est pas lancé. |
| `docker compose` : port `54329` déjà utilisé | Une autre base occupe le port : l'arrêter, ou `docker ps` pour la trouver. |
| `dotnet run` : adresse `5080` déjà utilisée | Une API tourne déjà dans un autre terminal : l'arrêter (`lsof -i :5080`). |
| `dotnet run` : erreur de connexion à la base | La base n'est pas démarrée ou pas prête : `docker compose up -d --wait` dans `api/`. |
| `npm ci` échoue sur la version de Node | Node trop ancien : installer Node 22.13 ou plus récent. |
| L'application affiche des erreurs réseau | L'API n'est pas lancée, ou `EXPO_PUBLIC_API_URL` dans `mobile/.env` ne pointe pas vers elle. |
| Un écran plante juste après un changement de l'API | L'API n'a pas été relancée : voir « Modifier un point d'entrée de l'API ». |
| Pas de « Connexion de test » | `EXPO_PUBLIC_DEV_LOGIN=true` manque dans `mobile/.env` ; relancer `npm start -- --clear`. |
| Comportement étrange après un changement de `.env` | Cache de Metro : `npm start -- --clear`. |
| `xcrun simctl` : « No devices are booted » | Ouvrir l'app Simulator (ou appuyer sur `i` dans `npm start`). |

Autres cas propres à l'API (Testcontainers, MinIO, premier démarrage lent) : [README de l'API](api/README.md).

## Documentation

| Document | Contenu |
| --- | --- |
| [Périmètre V1](docs/perimetre-v1.md) | Ce que fait la V1 et ce qui en est exclu |
| [Spécification technique](docs/specification-v1.md) | Modèle de données, règles métier, points d'entrée |
| [Plan de développement](docs/plan.md) | Les tâches, leur état, et ce qu'il reste à faire |
| [Plan : vues de l'onglet Balades](docs/plan-panneau-balades.md) | Panneau coulissant et liste ; ce qui reste à faire |
| [Décisions d'architecture](docs/adr/README.md) | Un fichier par décision technique |
| [Guide de déploiement](docs/deploiement.md) | Mettre l'API en ligne : image, migrations, sauvegardes, hébergeurs |
| [Démo hébergée](docs/demo-hebergee.md) | Mettre l'API de démonstration en ligne gratuitement et la montrer depuis un iPhone |
| [Revue de la V1](docs/revue-v1.md) | Constats de la revue générale et suites données |
| [API](api/README.md) et [mobile](mobile/README.md) | Commandes, configuration et conventions de chaque partie |
