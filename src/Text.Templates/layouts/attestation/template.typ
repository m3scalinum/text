// Attestation (attestation de travail or de paiement) — A4.
//
// Every word the reader sees comes from `p.labels`: the same layout renders in English or in
// French. The host appends `#render((…))` with the bound parameters, so this file stays a valid
// Typst document on its own: it only defines the function.

#let render(p) = {
  let labels = p.labels
  let employer = p.employer
  let employee = p.employee
  let accent = rgb("#3b3b6d")
  let muted = luma(115)
  let rule = luma(200)

  let start-date = employee.at("start_date", default: none)

  set document(title: p.title, author: employer.name)
  set text(size: 10.5pt)
  set par(justify: true, leading: 0.8em)

  set page(
    paper: "a4",
    margin: (x: 2.2cm, top: 2.4cm, bottom: 2.4cm),
    footer: context {
      set text(size: 7pt, fill: luma(130))
      line(length: 100%, stroke: 0.4pt + rule)
      v(3pt)
      grid(
        columns: (1fr, auto),
        [#employer.at("legal", default: "")],
        [#counter(page).get().first()],
      )
    },
  )

  // Letterhead.
  grid(
    columns: (1fr, auto),
    align: (left, top),
    [
      #text(size: 13pt, weight: "bold", fill: accent)[#employer.name] \
      #if employer.at("address", default: "") != "" { [#employer.address #linebreak()] }
      #if employer.at("contact", default: "") != "" { [#employer.contact] }
    ],
    align(right)[
      #if p.place != "" {
        text(size: 9pt, fill: muted)[#p.place, #p.issued.display(labels.at("date_format"))]
      } else {
        text(size: 9pt, fill: muted)[#p.issued.display(labels.at("date_format"))]
      }
    ],
  )

  v(2.2em)
  line(length: 100%, stroke: 0.8pt + accent)
  v(2em)

  align(center)[
    #text(size: 18pt, weight: "bold", fill: accent)[#upper(labels.at("document"))]
    #v(4pt)
    #text(size: 10pt, fill: muted)[#p.title]
  ]

  v(2.4em)

  // The certifying clauses. Each sentence is a label; only the names, dates and position it
  // interpolates come from the record.
  let representative = employer.at("legal_representative", default: "")
  let position = employee.at("position", default: "")
  let contract = employee.at("contract", default: "")

  par[
    #labels.at("intro") #strong[#employer.name]#if representative != "" [#labels.at("represented_by") #strong[#representative]]#labels.at("certifies") #strong[#employee.name]#if position != "" [#labels.at("holds_position") #strong[#position]].
  ]

  v(0.9em)

  if start-date != none {
    par[
      #labels.at("employed_since") #start-date.display(labels.at("date_format"))#if contract != "" [, #contract].
    ]
    v(0.9em)
  }

  par[
    #labels.at("purpose_intro") #strong[#p.purpose].
  ]

  v(0.9em)

  par[#labels.at("closing")]

  v(3.2em)

  grid(
    columns: (1fr, 1fr),
    column-gutter: 24pt,
    [
      #line(length: 100%, stroke: 0.5pt + luma(120))
      #v(3pt)
      #text(size: 8pt, fill: muted)[#labels.at("signature")]
    ],
    [
      #line(length: 100%, stroke: 0.5pt + luma(120))
      #v(3pt)
      #text(size: 8pt, fill: muted)[#labels.at("stamp")]
    ],
  )
}
