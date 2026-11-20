// Invoice layout — US letter, US conventions (USD amounts, MM/DD/YYYY dates).
//
// The host appends `#render((…))` with the bound parameters, so this file stays a valid
// Typst document on its own: it only defines the function.

// 1234.5 -> "$1,234.50"
#let money(value, symbol: "$") = {
  let rounded = calc.round(calc.abs(value), digits: 2)
  let whole = calc.floor(rounded)
  let cents = calc.round((rounded - whole) * 100)
  let carry = cents == 100
  let whole-text = str(int(if carry { whole + 1 } else { whole }))
  let cents-value = int(if carry { 0 } else { cents })

  let count = whole-text.len()
  let grouped = whole-text
    .clusters()
    .enumerate()
    .map(((index, digit)) => if index > 0 and calc.rem(count - index, 3) == 0 { "," + digit } else { digit })
    .fold("", (acc, part) => acc + part)

  let cent-text = if cents-value < 10 { "0" + str(cents-value) } else { str(cents-value) }
  (if value < 0 { "-" } else { "" }) + symbol + grouped + "." + cent-text
}

// 3.0 -> "3", 2.5 -> "2.5"
#let quantity(value) = if calc.rem(value, 1) == 0 { str(int(value)) } else { str(value) }

#let us-date(value) = value.display("[month]/[day]/[year]")

#let join(values, separator) = values.fold("", (acc, value) => if acc == "" { value } else { acc + separator + value })

#let render(p) = {
  let seller = p.seller
  let customer = p.customer
  let items = p.items
  let logo = seller.at("logo", default: none)
  let accent = rgb("#1f3a5f")
  let muted = luma(110)

  let categories = items
    .map(item => item.at("category", default: "Line items"))
    .dedup()

  let subtotal = items.fold(0.0, (sum, item) => sum + item.quantity * item.unit_price)
  let taxable = items.fold(0.0, (sum, item) =>
    if item.at("taxable", default: true) { sum + item.quantity * item.unit_price } else { sum })
  let tax = taxable * p.tax_rate
  let total = subtotal + tax
  let balance = total - p.amount_paid
  let tax-label = "Sales tax (" + quantity(calc.round(p.tax_rate * 100, digits: 3)) + "%)"

  let contact = ("phone", "email", "website")
    .map(key => seller.at(key, default: ""))
    .filter(value => value != "")

  set document(title: p.title, author: seller.name)
  set text(size: 9.5pt)
  set par(justify: false)

  set page(
    paper: "us-letter",
    margin: (x: 0.8in, top: 1.45in, bottom: 1.3in),
    header: context {
      let page-number = counter(page).get().first()
      set text(size: 8pt, fill: muted)

      if page-number == 1 {
        grid(
          columns: (1fr, auto),
          align: (left, right),
          if logo != none {
            image(logo, width: 2.2in)
          } else {
            text(size: 17pt, weight: "bold", fill: accent)[#seller.name]
          },
          align(right)[
            #text(size: 9pt, weight: "bold", fill: luma(40))[#seller.name] \
            #for line in seller.at("address", default: ()) { [#line #linebreak()] }
            #join(contact, "  ·  ")
          ],
        )
      } else {
        grid(
          columns: (1fr, auto),
          align: (left, right),
          [#seller.name — #p.title],
          [Page #page-number of #counter(page).final().first()],
        )
      }
    },
    footer: context {
      set text(size: 7pt, fill: luma(120))
      v(6pt)
      line(length: 100%, stroke: 0.4pt + luma(200))
      v(4pt)
      grid(
        columns: (1fr, auto),
        align: (left + top, right + top),
        block(width: 5.2in)[#seller.at("legal", default: "")],
        [Page #counter(page).get().first() of #counter(page).final().first()],
      )
    },
  )

  // Masthead: title on the left, invoice identity on the right.
  grid(
    columns: (1fr, auto),
    align: (left, right),
    [
      #text(size: 26pt, weight: "bold", fill: accent)[INVOICE]
      #v(1pt)
      #text(size: 9pt, fill: muted)[#p.title]
    ],
    align(right)[
      #table(
        columns: (auto, auto),
        align: (left, right),
        inset: (x: 0pt, y: 2.5pt),
        column-gutter: 16pt,
        stroke: none,
        [#text(fill: muted)[Invoice no.]], [*#p.number*],
        [#text(fill: muted)[Issue date]], [#us-date(p.issued)],
        [#text(fill: muted)[Due date]], [#us-date(p.due)],
        [#text(fill: muted)[Currency]], [#p.currency],
      )
    ],
  )

  v(4pt)
  line(length: 100%, stroke: 1.2pt + accent)
  v(1.2em)

  // Bill-to and payment terms.
  grid(
    columns: (1fr, 1fr),
    column-gutter: 20pt,
    [
      #text(size: 7.5pt, weight: "bold", fill: muted)[BILL TO]
      #v(3pt)
      #text(weight: "bold")[#customer.name] \
      #if customer.at("contact", default: "") != "" { [#customer.contact #linebreak()] }
      #for line in customer.at("address", default: ()) { [#line #linebreak()] }
      #if customer.at("email", default: "") != "" { [#customer.email #linebreak()] }
      #if customer.at("account", default: "") != "" { [Account #customer.account] }
    ],
    [
      #text(size: 7.5pt, weight: "bold", fill: muted)[PAYMENT TERMS]
      #v(3pt)
      #seller.at("terms", default: "Net 30") \
      #if customer.at("po", default: "") != "" { [PO number #customer.po #linebreak()] }
      #if p.amount_paid > 0 { [Paid to date #money(p.amount_paid)] }
    ],
  )

  v(1.4em)

  // Line items, grouped by category, with a subtotal per group.
  for category in categories {
    let rows = items.filter(item => item.at("category", default: "Line items") == category)
    let group-total = rows.fold(0.0, (sum, item) => sum + item.quantity * item.unit_price)

    grid(
      columns: (1fr, auto),
      align: (left, right),
      text(size: 8pt, weight: "bold", fill: accent)[#upper(category)],
      text(size: 8pt, fill: muted)[#money(group-total)],
    )
    v(2pt)

    table(
      columns: (1fr, 0.35fr, 0.8fr, 0.9fr),
      align: (left, right, right, right),
      inset: (x: 6pt, y: 5pt),
      stroke: (x, y) => (
        top: if y == 0 { 0.6pt + luma(150) } else { 0pt },
        bottom: if y == 0 or y == rows.len() { 0.6pt + luma(150) } else { 0.3pt + luma(225) },
        left: none,
        right: none,
      ),
      table.header(
        text(size: 7pt, weight: "bold", fill: luma(90))[DESCRIPTION],
        text(size: 7pt, weight: "bold", fill: luma(90))[QTY],
        text(size: 7pt, weight: "bold", fill: luma(90))[UNIT PRICE],
        text(size: 7pt, weight: "bold", fill: luma(90))[AMOUNT],
      ),
      ..rows
        .map(item => (
          [
            #item.description
            #if item.at("sku", default: "") != "" {
              linebreak()
              text(size: 7.5pt, fill: muted)[#item.sku]
            }
            #if item.at("details", default: "") != "" {
              linebreak()
              text(size: 7.5pt, fill: muted)[#item.details]
            }
          ],
          quantity(item.quantity),
          money(item.unit_price),
          money(item.quantity * item.unit_price),
        ))
        .flatten(),
    )

    v(1em)
  }

  // Totals.
  align(right)[
    #block(width: 3.1in)[
      #table(
        columns: (1fr, auto),
        align: (right, right),
        inset: (x: 4pt, y: 3pt),
        stroke: none,
        [#text(fill: muted)[Subtotal]], [#money(subtotal)],
        [#text(fill: muted)[#tax-label]], [#money(tax)],
        table.hline(stroke: 0.5pt + luma(180)),
        [*Total*], [*#money(total)*],
        [#text(fill: muted)[Amount paid]], [#money(p.amount_paid)],
        table.hline(stroke: 0.5pt + luma(180)),
        [#text(weight: "bold", fill: accent)[Balance due]], [#text(weight: "bold", fill: accent)[#money(balance)]],
      )
    ]
  ]

  v(1.6em)

  // Notes and remittance.
  grid(
    columns: (1fr, 1fr),
    column-gutter: 20pt,
    [
      #text(size: 7.5pt, weight: "bold", fill: muted)[NOTES]
      #v(3pt)
      #if p.notes != "" { [#p.notes] } else { [Thank you for your business.] }
    ],
    [
      #text(size: 7.5pt, weight: "bold", fill: muted)[REMIT TO]
      #v(3pt)
      #seller.at("remit", default: seller.name) \
      #if seller.at("bank", default: "") != "" { [#seller.bank #linebreak()] }
      #if seller.at("tax_id", default: "") != "" { [Tax ID #seller.tax_id] }
    ],
  )

  v(2.2em)
  grid(
    columns: (1fr, 1fr),
    column-gutter: 20pt,
    [
      #line(length: 2.4in, stroke: 0.5pt + luma(120))
      #v(2pt)
      #text(size: 7.5pt, fill: muted)[Authorized signature]
    ],
    [
      #line(length: 2.4in, stroke: 0.5pt + luma(120))
      #v(2pt)
      #text(size: 7.5pt, fill: muted)[Date]
    ],
  )
}
