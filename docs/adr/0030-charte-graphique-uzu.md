# ADR 0030 — Charte graphique « Uzu » : blanc cassé, fauve et noir, couleurs du pelage d'un Jack Russell

- **Statut :** Accepté
- **Date :** 18/11/2026
- **Précisé par :** [ADR 0044](0044-logo-et-icone-uzu.md) (logo, icône et écran de démarrage).

## Contexte

Le porteur du produit trouve l'application belle mais fade, sans caractère et pas assez tournée vers le monde du chien. La palette d'origine (vert forêt en couleur principale, terre cuite en accent, neutres chauds) se confondait en plus avec la carte d'Apple Plans, verte, qu'on ne peut pas recolorer et qui occupe l'essentiel de l'onglet Balades. Un logo sera dessiné plus tard d'après sa chienne, une Jack Russell ; il n'est pas l'objet de cette décision.

## Décision

Le porteur du produit retient la piste « Uzu » : les couleurs du pelage d'un Jack Russell, blanc cassé, fauve et noir, avec du sable pour les teintes douces. Les jetons de `mobile/src/theme/tokens.ts` portent la palette, en clair et en sombre :

| Rôle | Clair | Sombre |
| --- | --- | --- |
| Fond (`background`) | `#FBF6EC` blanc cassé | `#14100D` noir chaud |
| Surfaces (`surface`, `surfaceMuted`) | `#FFFDF9`, `#F3EADC` | `#201914`, `#2C231C` |
| Texte (`text`, `textMuted`) | `#1F1A17`, `#62564A` | `#F6EEE2`, `#BFAE9A` |
| Principal (`primary`, texte dessus `onPrimary`) | `#9C5626` fauve, `#FFFFFF` | `#E0A06C` fauve éclairci, `#24140A` |
| Fond sélectionné (`primarySoft`) | `#F5E8D7` sable clair | `#3A291B` |
| Information (`info` sur `infoSoft`) | `#5C3A1C` sur `#E9C9A0` sable | `#E9C9A0` sur `#3B2E22` |
| Balades des professionnels (`pro`, `onPro`) | `#1F1A17` noir chaud, `#FBF6EC` | `#F3EADB` blanc cassé, `#1F1A17` |
| Danger (`danger`) | `#A8322A` brique | `#F2B3A8` |

- Le fauve de la maquette (`#A85F2A`) est légèrement assombri en `#9C5626` : le premier n'atteignait que 4,50:1 sur le fond et 3,8:1 sur le sable clair, trop juste pour un lien ou une pastille sélectionnée ; le second donne 5,6:1 avec le texte blanc, 5,2:1 sur le fond et 4,6:1 sur le sable clair. Le sable `#F1E2CF` de la maquette devient `#F5E8D7` pour la même raison.
- Le thème sombre est une palette à part : fond noir chaud (pas un gris bleuté), fauve éclairci, texte blanc cassé.
- Les balades des professionnels, marquées jusque-là en terre cuite (trop proche du fauve), passent en noir chaud en clair et en blanc cassé en sombre : badge « Pro », bordure de leur fiche et épingle. Le libellé « Pro » reste : la couleur n'est jamais le seul indice. Le jeton `accent` disparaît au profit de `pro`.
- Le verre teinté (bouton « + », pastille de filtre active) prend le fauve ; le verre non teinté porte une teinte légère du fond (`glassTint`) et le panneau un voile du fond à ses positions basse et moyenne, pour pencher vers le blanc cassé plutôt que vers le vert de la carte.
- Deux touches « chien », avec retenue : les épingles de la carte sont des ronds marqués d'une patte (symbole système `pawprint.fill`), et les états vides des balades montrent une patte.

## Conséquences

- Toute couleur passe par un jeton ; les paires texte/fond réellement utilisées sont vérifiées par `src/theme/__tests__/theme.test.ts` (4,5:1 pour le texte, 3:1 pour les icônes porteuses de sens et les bordures), en clair et en sombre.
- Les épingles sont dessinées par l'application (`WalkPin`) : les épingles natives de `react-native-maps` n'acceptent pas de glyphe. Elles perdent l'animation de sélection du système ; la balade sélectionnée a une épingle plus grande. Sur Android, une épingle dessinée coûte plus cher à afficher (non vérifié sur un appareil).
- Le contraste du texte sur le verre ne se calcule pas (le système adapte le verre au fond) : il a été vérifié à l'œil dans le simulateur, en clair et en sombre, sur la carte.
- Le logo, l'icône de l'application et l'écran de démarrage restent à refaire, d'après les photos de la chienne du porteur du produit ; ils sont hors de cette décision.

## Alternatives écartées

- **« Sous-bois »** : garder le vert en le réchauffant de fauve. Plus proche de l'existant, mais toujours confondu avec la carte et sans caractère propre.
- **« Truffe »** : noir et blanc avec un orange vif. Fort, mais plus froid et moins lié au chien ; l'orange vif fatigue sur de grandes surfaces.
