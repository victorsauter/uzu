# ADR 0044 — Logo, icône et écran de démarrage : la tête d'Uzu en aplats, sur fond sable

- **Statut :** Accepté
- **Date :** 22/11/2026
- **Note :** décision du porteur du projet, sur esquisses.
- **Précise :** [ADR 0030](0030-charte-graphique-uzu.md) (le logo y était renvoyé à plus tard).

## Contexte

L'application livrait encore l'icône et l'écran de démarrage du modèle d'Expo. L'ADR 0030 prévoyait un logo dessiné d'après la chienne du porteur du produit, une Jack Russell, à partir de photos. Il faut une icône iOS (1024 px, sans transparence), une icône adaptative Android (avant-plan dans un cercle sûr, couche monochrome pour les icônes à thème), une image d'écran de démarrage lisible en clair et en sombre, et un favicon, dans la palette « Uzu ».

## Décision

Le logo est la tête de la chienne, de face, dessinée en **aplats géométriques** d'après une photo : ses taches réelles (calotte noire, liste blanche du front à la truffe, joues fauves, oreille gauche tombante et oreille droite relevée), un visage arrondi qui s'affine vers le museau, des yeux avec un reflet et un petit sourire. Il est posé sur le **sable** de la palette (`#E9C9A0`, jeton `infoSoft`), seule couleur sur laquelle le blanc, le fauve et le noir de la tête ressortent tous les trois. Pas de texte dans l'icône, pas de collier.

- La source est un SVG versionné (`mobile/assets/brand/uzu-head.svg`). Toutes les images en sont exportées par un script (`npm run brand:export`, `@resvg/resvg-js` en dépendance de développement), qui vérifie les règles des plateformes avant d'écrire : icône iOS sans canal alpha, dessin dans le cercle de 66 dp sur 108 de l'icône adaptative, dessin dans les deux tiers centraux de l'image de démarrage.
- L'icône adaptative Android prend le sable en couleur de fond (`BRAND.iconBackground`) ; sa couche monochrome est la silhouette de la tête et des oreilles, yeux, truffe et bouche découpés.
- L'écran de démarrage montre un disque sable avec la tête, sur le fond blanc cassé en clair et noir chaud en sombre : une seule image pour les deux thèmes, largeur 200.

## Conséquences

- Changer le logo, c'est éditer le SVG puis relancer l'export ; un test vérifie la taille et la transparence de chaque image et la présence des parties du SVG utilisées par le script.
- Le mot « Uzu » n'apparaît pas sur l'écran de démarrage : Android 12 et plus ne montre que le cercle central de l'image, le texte y serait coupé.
- L'écran de démarrage n'est fidèle que sur une build de distribution ; Expo Go montre l'icône à la place (documentation d'`expo-splash-screen`). Le rendu sur un lanceur Android réel n'a pas été vérifié.
- L'icône de notification Android (`notification-icon.png`, silhouette blanche) n'est pas tirée du logo dans cette décision.

## Alternatives écartées

- **Silhouette de profil** et **monogramme « u » à oreilles** : plus sobres, mais moins liés à la chienne.
- **Dessin fidèle à la photo** (premier jet) : ressemblant, mais trop proche pour ne pas décevoir (« ça ressemble à mon chien, mais ce n'est pas mon chien »).
- **Variantes plus abstraites** : tête parfaitement ronde, ou réduite aux taches sans yeux ; jugées moins sympathiques. La tête en carré arrondi, plus mignonne, a été affinée vers le museau à la demande du porteur du produit.
- **Fonds blanc cassé, fauve ou noir** : la tête blanche, les joues fauves ou la calotte noire s'y perdaient.
