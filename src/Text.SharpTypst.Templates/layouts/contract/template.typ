// Contract (contrat de prestation or de travail) — A4.
//
// Every word the reader sees comes from `p.labels`: the same layout renders in English or in
// French without a second file. The host appends `#render((…))` with the bound parameters, so this
// file stays a valid Typst document on its own: it only defines the function.

#let render(p) = {
  let labels = p.labels
  let party-a = p.party_a
  let party-b = p.party_b
  let clauses = p.clauses
  let accent = rgb("#2b3a55")
  let muted = luma(115)
  let rule = luma(205)

  set document(title: p.title, author: party-a.name)
  set text(size: 10pt)
  set par(justify: true, leading: 0.8em)

  set page(
    paper: "a4",
    margin: (x: 2.0cm, top: 2.6cm, bottom: 2.4cm),
    header: context {
      set text(size: 8pt, fill: muted)
      grid(
        columns: (1fr, auto),
        align: (left, right),
        [#party-a.name — #party-b.name],
        [#labels.at("page") #counter(page).get().first() #labels.at("of_page") #counter(page).final().first()],
      )
    },
    footer: context {
      set text(size: 7pt, fill: luma(130))
      v(4pt)
      line(length: 100%, stroke: 0.4pt + rule)
      v(3pt)
      grid(
        columns: (1fr, auto),
        [#p.title],
        [#labels.at("page") #counter(page).get().first() #labels.at("of_page") #counter(page).final().first()],
      )
    },
  )

  // Masthead: what the document is, and the identity of this contract.
  grid(
    columns: (1fr, auto),
    align: (left, right),
    [
      #text(size: 24pt, weight: "bold", fill: accent)[#labels.at("document")]
      #v(2pt)
      #text(size: 9pt, fill: muted)[#p.title]
    ],
    align(right)[
      #table(
        columns: (auto, auto),
        align: (left, right),
        inset: (x: 0pt, y: 2.5pt),
        column-gutter: 14pt,
        stroke: none,
        [#text(fill: muted)[#labels.at("contract_number")]], [*#p.number*],
        [#text(fill: muted)[#labels.at("issue_date")]], [#p.issued.display(labels.at("date_format"))],
        [#text(fill: muted)[#labels.at("effective_date")]], [#p.effective.display(labels.at("date_format"))],
      )
    ],
  )

  v(6pt)
  line(length: 100%, stroke: 1.2pt + accent)
  v(1.4em)

  // The two parties, with the word that joins them supplied by the label set.
  let party(role, person) = [
    #text(size: 7.5pt, weight: "bold", fill: muted)[#upper(role)]
    #v(3pt)
    #text(weight: "bold")[#person.name] \
    #if person.at("registration", default: "") != "" { [#labels.at("registration") #person.registration #linebreak()] }
    #for line in person.at("address", default: ()) { [#line #linebreak()] }
    #if person.at("contact", default: "") != "" { [#person.contact #linebreak()] }
    #if person.at("representative", default: "") != "" { [#labels.at("representative") #person.representative] }
  ]

  grid(
    columns: (1fr, auto, 1fr),
    column-gutter: 18pt,
    align: (left, center, left),
    party(labels.at("party_a"), party-a),
    [#text(size: 8pt, fill: muted, weight: "bold")[#upper(labels.at("between"))]],
    party(labels.at("party_b"), party-b),
  )

  v(1.8em)
  line(length: 100%, stroke: 0.5pt + rule)
  v(0.4em)

  // The clauses, numbered in the order the caller gives them.
  grid(
    columns: (auto, 1fr),
    column-gutter: 8pt,
    align: (left, left),
    [#text(size: 7.5pt, weight: "bold", fill: muted)[#upper(labels.at("clauses"))]], [],
  )

  v(0.6em)

  for (index, clause) in clauses.enumerate() {
    grid(
      columns: (auto, 1fr),
      column-gutter: 8pt,
      align: (left, top),
      [#text(size: 8pt, weight: "bold", fill: accent)[#labels.at("article") #(index + 1)]],
      [
        #text(weight: "bold")[#clause.heading]
        #v(2pt)
        #par(justify: true)[#clause.text]
        #if clause.at("items", default: ()).len() > 0 {
          set par(justify: false)
          for item in clause.at("items", default: ()) {
            list.item(item)
          }
        }
      ],
    )
    v(0.9em)
  }

  // Signature block: one line per party, under the place and date it was signed.
  v(1.4em)
  if p.place != "" {
    par[#labels.at("signed_at") #p.place#labels.at("on") #p.issued.display(labels.at("date_format"))]
  } else {
    par[#p.issued.display(labels.at("date_format"))]
  }

  v(2.6em)

  let signature(person) = [
    #line(length: 100%, stroke: 0.5pt + luma(120))
    #v(3pt)
    #text(size: 8pt)[#person.name] \
    #text(size: 7.5pt, fill: muted)[#labels.at("signature")]
  ]

  grid(
    columns: (1fr, 1fr),
    column-gutter: 24pt,
    signature(party-a),
    signature(party-b),
  )

  if p.notes != "" {
    v(1.6em)
    text(size: 8pt, fill: muted)[#p.notes]
  }
}
