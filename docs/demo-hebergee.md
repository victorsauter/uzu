# Uzu — Mettre la démo en ligne gratuitement (Render + Supabase)

**Version :** 0.1 · 20/11/2026 · pour montrer Uzu depuis un iPhone, sans que l'ordinateur tourne

Ce guide s'adresse à une personne qui le fait pour la première fois. Il met en ligne une **API de démonstration** avec des données fictives, **gratuitement**, et suit l'[ADR 0043](adr/0043-demo-hebergee.md). Ce n'est **pas** un guide de production : pour une vraie mise en ligne, voir le [guide de déploiement](deploiement.md). La partie iPhone (build Xcode) est dans le [README du mobile](../mobile/README.md#build-de-démo-sur-un-iphone-avec-un-apple-id-gratuit).

**Mentions « non vérifié ».** La démo a été mise en ligne le 22/11/2026 en suivant ce guide, mais les mentions ci-dessous n'ont pas été relues depuis. À l'écriture du guide, aucun compte n'avait été créé ni rien déployé. Ce qui vient de la documentation officielle est lié à sa source ; ce que je n'ai pas pu confirmer est marqué **non vérifié** : les noms exacts des boutons peuvent avoir changé, et chaque plan gratuit peut évoluer. En cas de doute, la page officielle prime.

## 0. Vue d'ensemble

```
iPhone (build Release, Apple ID gratuit)
        │ HTTPS
        ▼
Render, service gratuit ── API .NET, ASPNETCORE_ENVIRONMENT=Demo
        │                         │
        ▼                         ▼
Supabase Postgres + PostGIS    Supabase Storage (API S3) : photos
        ▲
cron-job.org ── GET /api/v1/health toutes les 10 minutes
```

Ce qu'il faut avoir : un compte **GitHub** avec le dépôt `dogo` et la branche `feat/demo-hebergee` poussée, une adresse e-mail pour les trois comptes à créer (Supabase, Render, cron-job.org), `curl` et `openssl` dans le terminal du Mac, `jq` (facultatif, pour lire les réponses). Prévoir une heure la première fois, dont plusieurs minutes d'attente pendant la construction de l'image.

À garder dans un gestionnaire de mots de passe, **jamais dans le dépôt ni dans une conversation** : le mot de passe de la base, les clés S3, la clé de signature des jetons.

## 1. Supabase : la base et les photos

Dans l'ordre :

### 1.1 Créer le projet

1. Créer un compte sur [supabase.com](https://supabase.com), puis un projet **dans une région de l'Union européenne** (Francfort, `eu-central-1`, est la plus proche de Render, qu'on mettra aussi à Francfort).
2. Choisir un mot de passe de base de données **sans caractères spéciaux** (lettres et chiffres, 24 caractères : `openssl rand -hex 12`) : il se retrouve dans une chaîne de connexion où `;` et `=` posent problème. Le noter.
3. Noter la **référence du projet** (une suite de lettres, visible dans l'adresse du tableau de bord et dans les réglages) : on l'appelle `<ref>` plus bas.

Le plan gratuit permet deux projets actifs, 500 Mo de base, 1 Go de stockage ([facturation Supabase](https://supabase.com/docs/guides/platform/billing-on-supabase)).

### 1.2 Activer PostGIS

L'API en a besoin ; sa première migration exécute `CREATE EXTENSION IF NOT EXISTS postgis`, qui ne demande alors aucun droit de plus ([ADR 0038](adr/0038-image-et-migrations-en-production.md)). Dans le tableau de bord, **SQL Editor**, exécuter :

```sql
create extension if not exists postgis with schema extensions;
```

(ou Database > Extensions > `postgis`, en gardant le schéma `extensions` proposé ; [documentation PostGIS de Supabase](https://supabase.com/docs/guides/database/extensions/postgis)). Vérifier : `select postgis_version();` répond un numéro de version.

> **Non vérifié.** Avec PostGIS dans le schéma `extensions`, les migrations de l'API créent des colonnes de type `geography` sans nom de schéma : cela marche si `extensions` est dans le `search_path` du rôle qui se connecte, ce qui est le réglage habituel de Supabase mais que je n'ai pas essayé avec le rôle du pooler. Si la première migration échoue avec « type "geography" does not exist », exécuter dans le SQL Editor `alter database postgres set search_path to "$user", public, extensions;` puis redémarrer le service Render (section 2.5).

### 1.3 Le bucket des photos et les clés S3

1. **Storage** > nouveau bucket nommé `photos`, **privé** (ne pas cocher « public ») : l'API relaie les octets, le bucket n'a pas à être lisible de l'extérieur.
2. Page des réglages du stockage (`/dashboard/project/_/storage/settings`) : créer une **clé d'accès S3** (« S3 Access Keys » ; le libellé exact est **non vérifié**). Noter l'**Access Key ID** et la **Secret Access Key** : la clé secrète ne s'affiche qu'une fois.
3. Page de configuration S3 (`/dashboard/project/_/storage/s3`) : noter l'**adresse du point d'accès (endpoint)** et la **région**. L'adresse a la forme `https://<ref>.storage.supabase.co/storage/v1/s3` ([documentation S3 de Supabase](https://supabase.com/docs/guides/storage/s3/authentication)) ; la région est celle du projet, par exemple `eu-central-1`.

Cette clé donne **tous les droits sur tous les buckets** du projet et ignore les règles d'accès (RLS) : elle ne doit se trouver que dans la configuration de Render.

### 1.4 La chaîne de connexion à la base

1. Bouton **Connect** du tableau de bord, onglet des chaînes de connexion : choisir le **Session pooler** (port **5432**). Pourquoi pas la « connexion directe » : sur le plan gratuit elle n'est joignable qu'en IPv6, alors que le pooler de session l'est en IPv4 ; et pas le « Transaction pooler » (port 6543), qui ne convient pas à une API qui garde ses connexions ([documentation](https://supabase.com/docs/guides/database/connecting-to-postgres)). Je ne sais pas si Render sait joindre de l'IPv6 : **non vérifié**, d'où le pooler.
2. Copier l'**hôte** tel qu'il s'affiche : `aws-<n>-<région>.pooler.supabase.com`, dont le numéro `<n>` ne se devine pas.
3. Écrire la chaîne pour l'API (format Npgsql, pas l'URL `postgresql://` de Supabase) :

```
Host=aws-0-eu-central-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<ref>;Password=<mot de passe>;SSL Mode=Require;Trust Server Certificate=true
```

L'utilisateur est `postgres.<ref>` (avec le point et la référence), pas `postgres`. `SSL Mode=Require` chiffre la connexion ; `Trust Server Certificate=true` évite que l'API refuse le certificat de Supabase, qui n'est pas signé par une autorité connue du système ([Npgsql : sécurité](https://www.npgsql.org/doc/security.html), [Supabase : SSL](https://supabase.com/docs/guides/platform/ssl-enforcement)). **Non vérifié** : je n'ai pas essayé la connexion ; si l'API se plaint du certificat sans cette option, c'est qu'elle manquait ; c'est la chaîne la plus simple, mais elle ne vérifie pas l'identité du serveur. La variante stricte serait de fournir le certificat de Supabase par `Root Certificate=…` : elle demande de l'embarquer dans l'image, hors périmètre d'une démo.

### 1.5 (Facultatif) Fermer l'API de données de Supabase

Supabase expose par défaut une API REST sur les tables du schéma `public`, ce qui inclut celles de l'API Uzu. Nous ne nous en servons pas : dans les réglages du projet, section API (« Data API »), la désactiver si l'option est proposée (libellé **non vérifié**). Les données sont fictives, mais autant ne pas laisser une porte inutile.

## 2. Render : l'API

### 2.1 Créer le service

1. Créer un compte sur [render.com](https://render.com) et lui donner accès au dépôt GitHub `dogo` (Render propose d'installer son application GitHub sur le dépôt). Le plan gratuit peut demander une carte bancaire à l'inscription : **non vérifié**.
2. **New** > **Web Service** > dépôt `dogo`. Remplir (libellés **non vérifiés** à la lettre) :

| Champ | Valeur |
| --- | --- |
| Name | `uzu-demo` (donne l'adresse `https://uzu-demo.onrender.com`, ou voisine si le nom est pris) |
| Language / Runtime | **Docker** |
| Branch | `feat/demo-hebergee` (ou la branche à montrer) |
| Region | **Frankfurt (EU Central)** |
| Root Directory | `api` |
| Dockerfile Path | `./Dockerfile` |
| Docker Build Context Directory | `.` |
| Instance Type | **Free** |
| Health Check Path (Advanced) | `/api/v1/health` |

**Pourquoi ces valeurs.** Le Dockerfile et son contexte sont le dossier `api/` ([ADR 0038](adr/0038-image-et-migrations-en-production.md)) ; Render évalue le chemin du Dockerfile et le contexte **relativement au Root Directory** ([Render : dépôts multiples](https://render.com/docs/monorepo-support)). Le contrôle de santé lance une requête GET toutes les quelques secondes et attend un code 2xx ou 3xx en moins de 5 secondes ; au bout de 60 secondes d'échecs, Render redémarre l'instance ([Render : contrôles de santé](https://render.com/docs/health-checks)). Il utilise l'adresse `onrender.com` du service comme en-tête `Host`.

3. Ne pas cliquer sur « Create » avant la section suivante : les variables d'environnement se posent à la création (sinon le premier démarrage échoue, ce qui est sans gravité mais fait perdre cinq minutes).

### 2.2 Les variables d'environnement

Dans la section **Environment Variables** (marquer les secrets comme tels si Render le propose). Les clés sont celles de [`api/.env.example`](../api/.env.example) :

| Variable | Valeur | Remarque |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Demo` | Exactement ce mot. L'image fixe `Production` : cette variable l'emporte. |
| `PORT` | `8080` | L'image écoute sur 8080 ; Render route vers `PORT` (10000 par défaut), il faut donc l'aligner ([Render : ports](https://render.com/docs/web-services)). |
| `ConnectionStrings__Dogo` | la chaîne de la section 1.4 | Secret. |
| `Auth__Jwt__SigningKey` | une clé aléatoire | `openssl rand -base64 48` dans le terminal. Secret, 32 octets au moins. La clé de développement du dépôt est refusée. |
| `Auth__AllowDevLogin` | `true` | Active la connexion de test, que l'appli de démo utilise (« Se connecter comme… »). |
| `DemoData__Enabled` | `true` | Active `/dev/demo-data`. Exige `Auth__AllowDevLogin=true`. |
| `Storage__Provider` | `S3` | |
| `Storage__S3__ServiceUrl` | l'adresse de la section 1.3 | `https://<ref>.storage.supabase.co/storage/v1/s3` |
| `Storage__S3__Bucket` | `photos` | |
| `Storage__S3__Region` | la région de la section 1.3 | par exemple `eu-central-1` |
| `Storage__S3__AccessKeyId` | l'Access Key ID | Secret. |
| `Storage__S3__SecretAccessKey` | la Secret Access Key | Secret. |

**À ne pas poser** :

- `Admin__Emails__*` : en démo, n'importe qui peut se connecter par la connexion de test **avec l'e-mail de son choix** ; avec une adresse listée, il serait administrateur. Sans cette variable, personne ne l'est.
- `Notifications__Provider`, `Auth__Apple__Audiences__*`, `Auth__Google__Audiences__*` : inutiles en `Demo` (les notifications restent dans les journaux, la connexion Apple est retirée de l'appli de démo).
- `ASPNETCORE_URLS`, `ASPNETCORE_FORWARDEDHEADERS_ENABLED` : voir le [guide de déploiement](deploiement.md#2-variables-denvironnement).
- `AllowedHosts` : facultatif ; le laisser à sa valeur par défaut évite tout risque que le contrôle de santé de Render soit refusé (400). Si on le pose, mettre exactement `<service>.onrender.com`.

**Le proxy de Render (`Proxy__*`).** Render termine HTTPS et transmet à l'API en HTTP. L'API ne lit `X-Forwarded-For` que d'un proxy déclaré ([ADR 0031](adr/0031-durcissement-de-l-api.md)), et je n'ai **pas pu vérifier** quelle adresse ou quel réseau Render utilise pour joindre un service ni s'il envoie bien cet en-tête (la page de Render sur les en-têtes de requête est introuvable). **Ne pas poser `Proxy__*` pour la démo.** Conséquence, acceptable pour quelques personnes : l'API voit une seule adresse pour tous, donc la limitation de débit par adresse (`/auth`, 60 requêtes par minute) est **partagée** entre tous les visiteurs, et HSTS n'est pas envoyé (l'API ne sait pas que la requête était en HTTPS ; sans effet pour une appli native). Si des 429 apparaissent pendant une démonstration à plusieurs, relever la limite : `RateLimiting__Auth__PermitLimit=300`. Ne jamais déclarer `0.0.0.0/0` comme réseau de confiance : n'importe quel client choisirait son adresse.

### 2.3 Créer, attendre, vérifier

1. Cliquer sur **Create Web Service**. Render construit l'image du Dockerfile : **plusieurs minutes** (SDK .NET, publication, bundle de migrations). Les journaux défilent dans l'onglet Logs.
2. Au premier démarrage, l'API **applique les migrations** (`Applying migration…` dans les journaux) puis écoute. Le service passe à « Live » quand le contrôle de santé répond.
3. Si le déploiement échoue, lire le début des journaux : l'API nomme les erreurs de configuration (variable manquante, clé trop courte, `Storage:S3:Bucket is empty but Storage:Provider is S3`…).

### 2.4 Mises à jour

Par défaut, Render redéploie à chaque poussée sur la branche choisie. Les migrations suivantes s'appliquent au démarrage. Pour redéployer à la main : **Manual Deploy** > **Deploy latest commit**, ou **Restart** pour redémarrer sans reconstruire.

### 2.5 En cas de problème au démarrage

| Symptôme dans les journaux | Cause probable |
| --- | --- |
| `ConnectionStrings:Dogo is empty` | variable absente ou mal nommée (deux soulignés). |
| `Auth:Jwt:SigningKey is missing…` | clé absente ou de moins de 32 octets. |
| `Storage:S3:… is empty but Storage:Provider is S3` | une des cinq valeurs S3 manque. |
| `Auth:AllowDevLogin must not be enabled in the '…' environment` | `ASPNETCORE_ENVIRONMENT` n'est pas exactement `Demo`. |
| Erreur de certificat ou d'authentification SSL vers la base | `Trust Server Certificate=true` absent de la chaîne (section 1.4). |
| `password authentication failed` | mot de passe ou utilisateur (`postgres.<ref>`) faux. |
| `type "geography" does not exist` | voir la note de la section 1.2. |
| Render ne détecte pas de port ouvert (message exact **non vérifié**) | `PORT` différent de 8080. |

## 3. cron-job.org : garder l'API éveillée

Sans visite pendant **15 minutes**, un service gratuit de Render s'endort ; le réveil prend **environ une minute** ([Render : plan gratuit](https://render.com/docs/free)). Un appel toutes les 10 minutes l'évite, et comme `GET /api/v1/health` interroge la base, il y maintient aussi une activité (voir « Limites »).

1. Créer un compte gratuit sur [cron-job.org](https://cron-job.org) (je n'ai pas lu sa documentation : les libellés ci-dessous sont **non vérifiés**).
2. **Create cronjob** : titre `Uzu démo`, adresse `https://<service>.onrender.com/api/v1/health`, exécution **toutes les 10 minutes**, méthode GET, 200 attendu.
3. Activer l'avertissement par e-mail en cas d'échec répété : utile pour savoir qu'une base est en pause ou qu'un déploiement est cassé.

Le premier appel après un long sommeil peut dépasser le délai de cron-job.org : il suffit à réveiller le service, le suivant passera. La documentation de Render ne mentionne pas les appels de maintien en éveil ; elle dit seulement que tout trafic entrant remet le compteur à zéro. Quota : un service allumé en permanence consomme environ 744 heures par mois sur les **750** du plan gratuit, qui sont **partagées par tous les services gratuits** du compte : n'en avoir qu'un seul.

## 4. Vérifications avec curl

Remplacer l'adresse. Si le service dormait, la première commande peut mettre une minute.

```bash
API=https://<service>.onrender.com

# 1. L'API répond et la base est jointe (attendu : 200 et {"status":"Healthy"}).
curl -sS -i $API/api/v1/health

# 2. La connexion de test existe (attendu : 200 et un accessToken). Le préfixe « demo- » est réservé aux comptes générés.
curl -sS -X POST $API/api/v1/auth/dev -H 'Content-Type: application/json' \
  -d '{"subject":"essai","displayName":"Essai"}' | jq '{ok: (.accessToken != null), user: .user.displayName}'

# 3. Générer les données de démonstration, photos comprises (attendu : des « counts » non nuls et photosStored > 0).
#    Compter jusqu'à une ou deux minutes : les photos sont téléchargées puis rangées dans le bucket.
curl -sS -X POST $API/api/v1/dev/demo-data -H 'Content-Type: application/json' -d '{"photos":true}' \
  --max-time 300 | jq

# 4. Une photo est servie par l'API (attendu : 200 et un type image/…).
PHOTO=$(curl -sS $API/api/v1/dev/demo-data | jq -r '[.accounts[] | select(.photoUrl != null)][0].photoUrl')
echo $PHOTO
curl -sS -o /dev/null -w '%{http_code} %{content_type}\n' "$API$PHOTO"
```

Puis le test qui prouve que les photos ne sont pas sur le disque éphémère de Render :

```bash
# 5. Dans Render : Manual Deploy > Restart, attendre « Live », puis refaire la commande 4 avec le même $PHOTO.
#    Attendu : toujours 200. Les données (commande 3) sont encore là : GET $API/api/v1/dev/demo-data donne "exists": true.
curl -sS -o /dev/null -w '%{http_code} %{content_type}\n' "$API$PHOTO"
```

Autres contrôles utiles :

```bash
# 6. Une route protégée exige un jeton (attendu : 401).
curl -sS -o /dev/null -w '%{http_code}\n' $API/api/v1/me

# 7. Une photo inconnue : 404.
curl -sS -o /dev/null -w '%{http_code}\n' $API/photos/00000000000000000000000000000000.jpg
```

Dans le tableau de bord de Supabase, **Storage** > `photos` doit montrer des fichiers aux noms de 32 caractères après la commande 3.

Ensuite, côté iPhone : [README du mobile](../mobile/README.md#build-de-démo-sur-un-iphone-avec-un-apple-id-gratuit) avec `EXPO_PUBLIC_API_URL=https://<service>.onrender.com`.

## 5. Limites à connaître

- **Réveil.** Sans appel, l'API s'endort après 15 minutes et met environ une minute à revenir. Avec le ping de la section 3, c'est rare. Render peut aussi **redémarrer** un service gratuit à tout moment : l'appli doit tolérer une première requête lente ou en erreur.
- **Pause de la base.** Un projet Supabase gratuit **sans activité pendant une semaine est mis en pause** ([Supabase : pause des projets](https://supabase.com/docs/guides/platform/free-project-pausing)). Le ping appelle `/health`, qui ouvre une connexion à la base : cela devrait compter comme de l'activité, **non vérifié** (selon une réponse d'un membre de Supabase relevée en recherche, ce sont des requêtes réelles qui comptent, pas les simples connexions). Une base en pause fait répondre 503 à `/health` et Render finit par redémarrer l'API en boucle : réactiver le projet depuis le tableau de bord de Supabase.
- **Quotas.** 750 heures de service par mois chez Render (un service allumé en continu en prend environ 744), 500 Mo de base et 1 Go de stockage chez Supabase. Render peut **suspendre** un service gratuit dont le trafic sortant est inhabituellement élevé (appels vers des bases ou du stockage d'objets compris), et suspend tous les services gratuits quand les heures ou la bande passante gratuites sont épuisées ([Render : plan gratuit](https://render.com/docs/free)).
- **Disque éphémère.** Tout fichier local de l'API disparaît à chaque redéploiement ou redémarrage : c'est pourquoi les photos sont dans Supabase Storage. La file de notifications en mémoire est perdue aussi (déjà le cas ailleurs).
- **L'API est ouverte à quiconque connaît son adresse.** La connexion de test laisse n'importe qui se connecter sous n'importe quel nom, et `/dev/demo-data` est anonyme : n'importe qui peut générer ou supprimer les données de démonstration, ou remplir la base. Ne mettre **que des données fictives**, ne pas diffuser l'adresse, ne jamais y inviter de vraies personnes. Pour fermer : supprimer le service Render (ou poser `Auth__AllowDevLogin=false` et `DemoData__Enabled=false`, mais l'appli de démo ne pourra plus se connecter).
- **Limitation de débit partagée** (voir la section 2.2, proxy).
- **Photos de démonstration.** Les images des comptes générés viennent de DiceBear et de Wikimedia Commons, certaines sous licence CC BY ou CC BY-SA ([README de l'API](../api/README.md#données-de-démonstration-développement)) : elles restent dans cette démo privée et ne se publient pas.
- **Apple ID gratuit.** La build iPhone cesse de s'ouvrir après 7 jours : voir le README du mobile.
- **Les plans changent.** Relire les pages liées avant de s'engager.

## 6. Tout arrêter

Supprimer le service dans Render, la tâche dans cron-job.org, mettre le projet Supabase en pause ou le supprimer, et supprimer la clé S3 (Storage > réglages). Rien de tout cela ne touche au dépôt.

## 7. Ce qui n'a pas pu être vérifié

- Tout le parcours réel : comptes Supabase, Render et cron-job.org, construction de l'image chez Render, première migration sur Supabase, photos dans Supabase Storage. Seul le stockage S3 a été essayé, **contre un MinIO local** (tests automatisés) ; Supabase Storage peut avoir des particularités que MinIO n'a pas.
- Les noms exacts des boutons et champs de Supabase, Render et cron-job.org.
- `Trust Server Certificate=true` pour la base de Supabase (section 1.4) et la résolution de `geography` avec PostGIS dans le schéma `extensions` (section 1.2).
- Le réseau et l'en-tête `X-Forwarded-For` du proxy de Render (section 2.2), donc aucune configuration `Proxy__*`.
- Que le ping de santé suffise à éviter la pause de la base de Supabase.
- Si Render demande une carte bancaire pour le plan gratuit.
- Les noms de propriétés `RequestChecksumCalculation` et `ResponseChecksumValidation` du kit AWS pour .NET : ils sont ceux de la version 4.1.2, avec laquelle le projet compile et dont les tests passent ; leur page de documentation n'a pas pu être lue.

## Sources

- Render : [plan gratuit](https://render.com/docs/free), [services web et ports](https://render.com/docs/web-services), [Docker](https://render.com/docs/docker), [dépôts multiples et dossier racine](https://render.com/docs/monorepo-support), [contrôles de santé](https://render.com/docs/health-checks).
- Supabase : [connexion à Postgres](https://supabase.com/docs/guides/database/connecting-to-postgres), [PostGIS](https://supabase.com/docs/guides/database/extensions/postgis), [S3 : authentification et adresse](https://supabase.com/docs/guides/storage/s3/authentication), [pause des projets gratuits](https://supabase.com/docs/guides/platform/free-project-pausing), [facturation et limites](https://supabase.com/docs/guides/platform/billing-on-supabase), [SSL](https://supabase.com/docs/guides/platform/ssl-enforcement).
- Npgsql : [sécurité et SSL](https://www.npgsql.org/doc/security.html), [paramètres de connexion](https://www.npgsql.org/doc/connection-string-parameters.html).
- [cron-job.org](https://cron-job.org) (documentation non lue).
