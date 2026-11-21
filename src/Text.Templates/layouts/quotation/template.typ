// Quotation (devis) — A4.
//
// Every word the reader sees comes from `p.labels`: the same layout renders in English or in
// French. The host appends `#render((…))` with the bound parameters, so this file stays a valid
// Typst document on its own: it only defines the function.

// 1234.5 -> "1 234,50" or "1,234.50", driven by the label set's separators.
#let money(value, labels) = {
  let rounded = calc.round(calc.abs(value), digits: 2)
  let whole = calc.floor(rounded)
  let cents = int(calc.round((rounded - whole) * 100))
  let carry = cents == 100
  let whole-text = str(int(if carry { whole + 1 } else { whole }))
  let cents-value = int(if carry { 0 } else { cents })
  let group = labels.at("thousand_separator")
  let point = labels.at("decimal_separator")
  let count = whole-text.len()

  let grouped = whole-text
    .clusters()
    .enumerate()
    .map(((index, digit)) => if index > 0 and group != "" and calc.rem(count - index, 3) == 0 {
      group + digit
    } else {
      digit
    })
    .fold("", (acc, part) => acc + part)

  let cent-text = if cents-value < 10 { "0" + str(cents-value) } else { str(cents-value) }
  (if value < 0 { "-" } else { "" }) + grouped + point + cent-text
}

// 3.0 -> "3", 2.5 -> "2.5"
#let quantity(value) = if calc.rem(value, 1) == 0 { str(int(value)) } else { str(value) }

#let render(p) = {
  let labels = p.labels
  let seller = p.seller
  let customer = p.customer
  let items = p.items
  let accent = rgb("#2f4858")
  let muted = luma(115)
  let rule = luma(205)

  let subtotal = items.fold(0.0, (sum, item) => sum + item.quantity * item.unit_price)
  let tax = subtotal * p.tax_rate
  let total = subtotal + tax
  let tax-label = labels.at("tax") + " (" + quantity(calc.round(p.tax_rate * 100, digits: 3)) + "%)"

  let contact = ("phone", "email", "website")
    .map(key => seller.at(key, default: ""))
    .filter(value => value != "")
    .fold("", (acc, value) => if acc == "" { value } else { acc + "  ·  " + value })

  set document(title: p.title, author: seller.name)
  set text(size: 9.5pt)
  set par(justify: false, leading: 0.7em)

  set page(
    paper: "a4",
    margin: (x: 1.7cm, top: 3.1cm, bottom: 2.4cm),
    header: context {
      let page-number = counter(page).get().first()
      set text(size: 8pt, fill: muted)

      if page-number == 1 {
        grid(
          columns: (1fr, auto),
          align: (left, right),
          [#text(size: 17pt, weight: "bold", fill: accent)[#seller.name]],
          align(right)[
            #text(size: 9pt, weight: "bold", fill: luma(40))[#p.title] \
            [#p.number]
            #if contact != "" { linebreak(); contact }
          ],
        )
      } else {
        grid(
          columns: (1fr, auto),
          align: (left, right),
          [#seller.name — #p.title],
          [#labels.at("page") #page-number #labels.at("of_page") #counter(page).final().first()],
        )
      }
    },
    footer: context {
      set text(size: 7pt, fill: luma(130))
      v(4pt)
      line(length: 100%, stroke: 0.4pt + rule)
      v(3pt)
      grid(
        columns: (1fr, auto),
        [#seller.at("legal", default: "")],
        [#labels.at("page") #counter(page).get().first() #labels.at("of_page") #counter(page).final().first()],
      )
    },
  )

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
        [#text(fill: muted)[#labels.at("quote_number")]], [*#p.number*],
        [#text(fill: muted)[#labels.at("issue_date")]], [#p.issued.display(labels.at("date_format"))],
        [#text(fill: muted)[#labels.at("valid_until")]], [#p.valid_until.display(labels.at("date_format"))],
        [#text(fill: muted)[#labels.at("currency")]], [#p.currency],
      )
    ],
  )

  v(6pt)
  line(length: 100%, stroke: 1.2pt + accent)
  v(1.2em)

  grid(
    columns: (1fr, 1fr),
    column-gutter: 20pt,
    [
      #text(size: 7.5pt, weight: "bold", fill: muted)[#upper(labels.at("prepared_for"))]
      #v(3pt)
      #text(weight: "bold")[#customer.name] \
      #if customer.at("contact", default: "") != "" { [#customer.contact #linebreak()] }
      #for line in customer.at("address", default: ()) { [#line #linebreak()] }
      #if customer.at("email", default: "") != "" { [#customer.email] }
    ],
    [
      #text(size: 7.5pt, weight: "bold", fill: muted)[#upper(labels.at("prepared_by"))]
      #v(3pt)
      #text(weight: "bold")[#seller.name] \
      #if seller.at("contact", default: "") != "" { [#seller.contact #linebreak()] }
      #for line in seller.at("address", default: ()) { [#line #linebreak()] }
      #if contact != "" { [#contact] }
    ],
  )

  v(1.4em)

  table(
    columns: (1fr, 0.35fr, 0.8fr, 0.9fr),
    align: (left, right, right, right),
    inset: (x: 6pt, y: 5pt),
    stroke: (x, y) => (
      top: if y == 0 { 0.6pt + luma(150) } else { 0pt },
      bottom: if y == 0 or y == items.len() { 0.6pt + luma(150) } else { 0.3pt + luma(225) },
      left: none,
      right: none,
    ),
    table.header(
      text(size: 7pt, weight: "bold", fill: luma(90))[#upper(labels.at("description"))],
      text(size: 7pt, weight: "bold", fill: luma(90))[#upper(labels.at("quantity"))],
      text(size: 7pt, weight: "bold", fill: luma(90))[#upper(labels.at("unit_price"))],
      text(size: 7pt, weight: "bold", fill: luma(90))[#upper(labels.at("amount"))],
    ),
    ..items
      .map(item => (
        [
          #item.description
          #if item.at("details", default: "") != "" {
            linebreak()
            text(size: 7.5pt, fill: muted)[#item.details]
          }
        ],
        quantity(item.quantity),
        money(item.unit_price, labels),
        money(item.quantity * item.unit_price, labels),
      ))
      .flatten(),
  )

  v(1em)

  align(right)[
    #block(width: 7.2cm)[
      #table(
        columns: (1fr, auto),
        align: (right, right),
        inset: (x: 4pt, y: 3pt),
        stroke: none,
        [#text(fill: muted)[#labels.at("subtotal")]], [#p.currency #money(subtotal, labels)],
        [#text(fill: muted)[#tax-label]], [#p.currency #money(tax, labels)],
        table.hline(stroke: 0.5pt + luma(180)),
        [#text(weight: "bold", fill: accent)[#labels.at("total")]],
        [#text(weight: "bold", fill: accent)[#p.currency #money(total, labels)]],
      )
    ]
  ]

  v(1.4em)

  grid(
    columns: (1fr, 1fr),
    column-gutter: 20pt,
    [
      #text(size: 7.5pt, weight: "bold", fill: muted)[#upper(labels.at("notes"))]
      #v(3pt)
      #if p.notes != "" { [#p.notes] } else { [#labels.at("validity")] }
    ],
    [
      #text(size: 7.5pt, weight: "bold", fill: muted)[#upper(labels.at("acceptance"))]
      #v(3pt)
      #labels.at("signature") \
      #v(26pt)
      #line(length: 100%, stroke: 0.5pt + luma(120))
      #v(2pt)
      #text(size: 7.5pt, fill: muted)[#labels.at("signature")]
    ],
  )
}
