# Documentation d'Uzu

Uzu est une application mobile pour trouver et organiser des balades entre chiens, en sachant à l'avance quels chiens seront là. Le dépôt, le code et l'infrastructure gardent le nom `dogo` (ADR 0041).

| Document | Contenu |
| --- | --- |
| [Périmètre V1](perimetre-v1.md) | Ce que fait la V1, ce qui en est exclu, les décisions produit |
| [Spécification technique](specification-v1.md) | Modèle de données, règles métier, points d'entrée de l'API ; version 0.3, à jour des décisions de la revue (ADR 0031 à 0037) et des chiens reliés à plusieurs humains (ADR 0042) |
| [Plan de développement](plan.md) | Les 31 tâches, leur difficulté et leur répartition en vagues ; la V1 est développée, ce qui reste est listé en fin de document |
| [Plan : vues de l'onglet Balades](plan-panneau-balades.md) | Deux vues au choix, panneau coulissant par défaut et liste ; premier lot livré sur iPhone, Android, lecteur d'écran et essais restent à faire |
| [Guide de déploiement](deploiement.md) | Mettre l'API en ligne : image, migrations, sauvegardes, hébergeurs ; l'hébergeur de production n'est pas choisi |
| [Démo hébergée](demo-hebergee.md) | Mettre l'API de démonstration en ligne gratuitement (Render, Supabase, cron-job.org) pour la montrer depuis un iPhone, Mac éteint ; guide pas à pas |
| [Revue de la V1](revue-v1.md) | Constats de la revue générale du code et de la documentation, et suites données |
| [Décisions d'architecture](adr/README.md) | Un fichier par décision technique, avec son contexte et ses alternatives |

## Tenir la documentation à jour

- Une décision produit change : modifier le périmètre, et le plan si un lot est touché.
- Une décision technique est prise ou révisée : ajouter un ADR, sans réécrire les anciens.
- Une tâche est terminée : mettre à jour la section État du plan.
