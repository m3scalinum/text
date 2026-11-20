// Model — every item of the category, invoked once.
#set document(title: "Conformance", author: "SharpTypst")
#set heading(numbering: "1.")

= Heading <model-heading>

A paragraph.

- bullet
- list

+ numbered
+ list

/ Term: description

#table(columns: 2, [a], [b])

#figure([content], caption: [A figure]) <model-figure>

#quote(block: true)[A quote.]

A footnote#footnote[A note.].

See @model-figure and #ref(<model-heading>).

#outline()

#parbreak()

#divider()

*strong* and _emph_.

#bibliography("refs.bib")

#cite(<doe2026>)

