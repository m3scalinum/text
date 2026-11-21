// The source behind extract.pdf. See the fixture README for how it was exported.
#set page(paper: "a4", margin: 2cm)
#set text(size: 11pt)

= Text extraction fixture

ZEBRAALPHA marks the first page. A sentence with a note#footnote[ZEBRANOTE] whose text sits at the
bottom of the page.

#table(
  columns: 3,
  [Column], [Second], [Third],
  [alpha], [beta], [gamma],
  [delta], [epsilon], [zeta],
)

#pagebreak()

ZEBRABETA marks the second page.

#table(
  columns: 2,
  [Key], [Value],
  [one], [1],
)
