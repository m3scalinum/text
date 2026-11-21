// Account statement (relevé de compte) — A4.
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

// A movement with no debit and no credit prints an empty cell, not a zero.
#let amount(value, labels) = if value == 0 { "" } else { money(value, labels) }

#let render(p) = {
  let labels = p.labels
  let account = p.account
  let accent = rgb("#1d4e4a")
  let muted = luma(115)
  let rule = luma(205)
  let opening = p.opening_balance * 1.0

  // One pass to carry the balance forward: each row keeps the movement and the balance after it.
  let ledger = p.transactions.fold(
    (balance: opening, rows: ()),
    (state, entry) => {
      let debit = entry.at("debit", default: 0) * 1.0
      let credit = entry.at("credit", default: 0) * 1.0
      let balance = state.balance - debit + credit
      (balance: balance, rows: state.rows + ((entry: entry, balance: balance),))
    },
  )

  let total-debit = p.transactions.fold(0.0, (sum, entry) => sum + entry.at("debit", default: 0))
  let total-credit = p.transactions.fold(0.0, (sum, entry) => sum + entry.at("credit", default: 0))

  set document(title: p.title, author: account.holder)
  set text(size: 9.5pt)
  set par(justify: false, leading: 0.7em)

  set page(
    paper: "a4",
    margin: (x: 1.6cm, top: 3.0cm, bottom: 2.3cm),
    header: context {
      let page-number = counter(page).get().first()
      set text(size: 8pt, fill: muted)

      if page-number == 1 {
        grid(
          columns: (1fr, auto),
          align: (left, right),
          [
            #text(size: 20pt, weight: "bold", fill: accent)[#labels.at("document")]
            #v(2pt)
            #text(size: 9pt)[#account.holder]
          ],
          align(right)[
            #text(size: 9pt, weight: "bold", fill: luma(40))[#p.title] \
            [#p.number]
          ],
        )
      } else {
        grid(
          columns: (1fr, auto),
          align: (left, right),
          [#account.holder — #p.number],
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
        [#account.at("legal", default: "")],
        [#labels.at("page") #counter(page).get().first() #labels.at("of_page") #counter(page).final().first()],
      )
    },
  )

  // Account identity.
  grid(
    columns: (1fr, 1fr, auto),
    column-gutter: 18pt,
    align: (left, left, right),
    [
      #text(size: 7.5pt, weight: "bold", fill: muted)[#upper(labels.at("holder"))]
      #v(3pt)
      #text(weight: "bold")[#account.holder] \
      #if account.at("address", default: "") != "" { [#account.address #linebreak()] }
      #if account.at("branch", default: "") != "" { [#account.branch] }
    ],
    [
      #text(size: 7.5pt, weight: "bold", fill: muted)[#upper(labels.at("account_number"))]
      #v(3pt)
      #account.number \
      #if account.at("iban", default: "") != "" { [#labels.at("iban") #account.iban #linebreak()] }
      #if account.at("bic", default: "") != "" { [#labels.at("bic") #account.bic] }
    ],
    align(right)[
      #table(
        columns: (auto, auto),
        align: (left, right),
        inset: (x: 0pt, y: 2.5pt),
        column-gutter: 12pt,
        stroke: none,
        [#text(fill: muted)[#labels.at("period")]],
        [#p.period_start.display(labels.at("date_format")) – #p.period_end.display(labels.at("date_format"))],
        [#text(fill: muted)[#labels.at("issued_on")]], [#p.issued.display(labels.at("date_format"))],
        [#text(fill: muted)[#labels.at("currency")]], [#p.currency],
      )
    ],
  )

  v(6pt)
  line(length: 100%, stroke: 1.2pt + accent)
  v(1em)

  // Opening balance, stated before the movements so every running balance can be checked.
  grid(
    columns: (1fr, auto),
    align: (left, right),
    [#text(size: 8pt, fill: muted)[#labels.at("opening_balance")]],
    [#text(weight: "bold")[#p.currency #money(opening, labels)]],
  )
  v(8pt)

  table(
    columns: (1fr, 2.4fr, 1.2fr, 0.9fr, 0.9fr, 1fr),
    align: (left, left, left, right, right, right),
    inset: (x: 5pt, y: 4.5pt),
    stroke: (x, y) => (
      top: if y == 0 { 0.6pt + luma(150) } else { 0pt },
      bottom: if y == 0 { 0.6pt + luma(150) } else { 0.3pt + luma(225) },
      left: none,
      right: none,
    ),
    table.header(
      text(size: 7pt, weight: "bold", fill: luma(90))[#upper(labels.at("date"))],
      text(size: 7pt, weight: "bold", fill: luma(90))[#upper(labels.at("description"))],
      text(size: 7pt, weight: "bold", fill: luma(90))[#upper(labels.at("reference"))],
      text(size: 7pt, weight: "bold", fill: luma(90))[#upper(labels.at("debit"))],
      text(size: 7pt, weight: "bold", fill: luma(90))[#upper(labels.at("credit"))],
      text(size: 7pt, weight: "bold", fill: luma(90))[#upper(labels.at("balance"))],
    ),
    ..ledger
      .rows
      .map(row => (
        row.entry.date.display("[year]-[month]-[day]"),
        row.entry.description,
        row.entry.at("reference", default: ""),
        amount(row.entry.at("debit", default: 0), labels),
        amount(row.entry.at("credit", default: 0), labels),
        money(row.balance, labels),
      ))
      .flatten(),
  )

  v(1em)

  // Closing summary.
  align(right)[
    #block(width: 8.2cm)[
      #table(
        columns: (1fr, auto),
        align: (right, right),
        inset: (x: 4pt, y: 3pt),
        stroke: none,
        [#text(fill: muted)[#labels.at("total_debits")]], [#p.currency #money(total-debit, labels)],
        [#text(fill: muted)[#labels.at("total_credits")]], [#p.currency #money(total-credit, labels)],
        table.hline(stroke: 0.5pt + luma(180)),
        [#text(weight: "bold", fill: accent)[#labels.at("closing_balance")]],
        [#text(weight: "bold", fill: accent)[#p.currency #money(ledger.balance, labels)]],
      )
    ]
  ]

  v(1.4em)

  if p.notes != "" {
    text(size: 8pt, fill: muted)[#p.notes]
  }
}
