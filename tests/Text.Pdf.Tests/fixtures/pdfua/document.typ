// The source behind ua1.pdf, a2b.pdf and a4.pdf. See README.md for the export options.
#set document(
  title: "Rapport de conformite accessibilite",
  author: "M3scalinum",
  date: datetime(year: 2026, month: 1, day: 15),
)
#set page(paper: "a4", margin: 2cm)
#set text(lang: "fr", size: 11pt)

= Rapport de conformité

Un document produit par le compilateur, avec un titre, une langue, une figure décrite et un
tableau. Il sert de fixture positive : chaque contrôle du vérificateur doit y passer.

#figure(
  image("logo.png", width: 2cm, alt: "Un carré rouge de quatre pixels"),
  caption: [Le logo du produit],
)

== Résultats

#table(
  columns: 3,
  [Contrôle], [Attendu], [Obtenu],
  [Arbre de structure], [Présent], [Oui],
  [Langue], [Déclarée], [Oui],
  [Polices], [Incorporées], [Oui],
)

#pagebreak()
#place(bottom + center)[Page deux, pour que le document ait plusieurs pages.]
