// Payslip (bulletin de paie) — A4.
//
// Every word the reader sees comes from `p.labels`, so the same layout renders in English or in
// French without a second file. The host appends `#render((…))` with the bound parameters, so this
// file stays a valid Typst document on its own: it only defines the function.

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

// A rate prints as a percentage of the base, rounded to two decimals.
#let percent(value, labels) = money(value * 100, labels) + " %"

#let render(p) = {
  let labels = p.labels
  let employer = p.employer
  let employee = p.employee
  let earnings = p.earnings
  let contributions = p.contributions
  let accent = rgb("#3f4d3a")
  let muted = luma(115)
  let rule = luma(205)

  let gross = earnings.fold(0.0, (sum, line) => sum + line.amount)
  let withheld = contributions.fold(0.0, (sum, line) => sum + line.amount)
  let net = gross - withheld

  set document(title: p.title, author: employer.name)
  set text(size: 9.5pt)
  set par(justify: false, leading: 0.7em)

  set page(
    paper: "a4",
    margin: (x: 1.7cm, top: 3.0cm, bottom: 2.4cm),
    header: context {
      let page-number = counter(page).get().first()
      set text(size: 8pt, fill: muted)

      if page-number == 1 {
        grid(
          columns: (1fr, auto),
          align: (left, right),
          [
            #text(size: 16pt, weight: "bold", fill: accent)[#employer.name]
            #if employer.at("address", default: "") != "" {
              linebreak()
              text(size: 8pt)[#employer.address]
            }
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
          [#employee.name — #p.number],
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
        [#employer.at("legal", default: "")],
        [#labels.at("page") #counter(page).get().first() #labels.at("of_page") #counter(page).final().first()],
      )
    },
  )

  // Masthead.
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
        [#text(fill: muted)[#labels.at("payslip_number")]], [*#p.number*],
        [#text(fill: muted)[#labels.at("issue_date")]], [#p.issued.display(labels.at("date_format"))],
        [#text(fill: muted)[#labels.at("period")]],
        [#p.period_start.display(labels.at("date_format")) — #p.period_end.display(labels.at("date_format"))],
      )
    ],
  )

  v(6pt)
  line(length: 100%, stroke: 1.2pt + accent)
  v(1.2em)

  // The two parties.
  grid(
    columns: (1fr, 1fr),
    column-gutter: 20pt,
    [
      #text(size: 7.5pt, weight: "bold", fill: muted)[#upper(labels.at("employer"))]
      #v(3pt)
      #text(weight: "bold")[#employer.name] \
      #if employer.at("address", default: "") != "" { [#employer.address #linebreak()] }
      #if employer.at("establishment", default: "") != "" { [#employer.establishment #linebreak()] }
      #if employer.at("registration", default: "") != "" { [#labels.at("registration") #employer.registration] }
    ],
    [
      #text(size: 7.5pt, weight: "bold", fill: muted)[#upper(labels.at("employee"))]
      #v(3pt)
      #text(weight: "bold")[#employee.name] \
      #if employee.at("position", default: "") != "" { [#employee.position #linebreak()] }
      #if employee.at("number", default: "") != "" { [#labels.at("employee_number") #employee.number #linebreak()] }
      #if employee.at("start_date", default: none) != none {
        [#labels.at("start_date") #employee.start_date.display(labels.at("date_format")) #linebreak()]
      }
      #if employee.at("address", default: "") != "" { [#employee.address] }
    ],
  )

  v(1.4em)

  // Gross earnings.
  let line-table(rows) = table(
    columns: (1fr, 0.8fr, 0.6fr, 0.9fr),
    align: (left, right, right, right),
    inset: (x: 6pt, y: 5pt),
    stroke: (x, y) => (
      top: if y == 0 { 0.6pt + luma(150) } else { 0pt },
      bottom: if y == 0 or y == rows.len() { 0.6pt + luma(150) } else { 0.3pt + luma(225) },
      left: none,
      right: none,
    ),
    table.header(
      text(size: 7pt, weight: "bold", fill: luma(90))[#upper(labels.at("description"))],
      text(size: 7pt, weight: "bold", fill: luma(90))[#upper(labels.at("base"))],
      text(size: 7pt, weight: "bold", fill: luma(90))[#upper(labels.at("rate"))],
      text(size: 7pt, weight: "bold", fill: luma(90))[#upper(labels.at("amount"))],
    ),
    ..rows
      .map(row => (
        row.label,
        if row.at("base", default: 0) > 0 { money(row.base, labels) } else { "" },
        if row.at("rate", default: 0) > 0 { percent(row.rate, labels) } else { "" },
        money(row.amount, labels),
      ))
      .flatten(),
  )

  text(size: 7.5pt, weight: "bold", fill: muted)[#upper(labels.at("earnings"))]
  v(3pt)
  line-table(earnings)

  v(1.2em)

  if contributions.len() > 0 {
    text(size: 7.5pt, weight: "bold", fill: muted)[#upper(labels.at("contributions"))]
    v(3pt)
    line-table(contributions)
    v(1.2em)
  }

  // Gross, withheld and net.
  align(right)[
    #block(width: 7.2cm)[
      #table(
        columns: (1fr, auto),
        align: (right, right),
        inset: (x: 4pt, y: 3pt),
        stroke: none,
        [#text(fill: muted)[#labels.at("gross_pay")]], [#p.currency #money(gross, labels)],
        [#text(fill: muted)[#labels.at("total_contributions")]], [#p.currency #money(-withheld, labels)],
        table.hline(stroke: 0.5pt + luma(180)),
        [#text(weight: "bold", fill: accent)[#labels.at("net_pay")]],
        [#text(weight: "bold", fill: accent)[#p.currency #money(net, labels)]],
      )
    ]
  ]

  v(1.2em)

  grid(
    columns: (1fr, 1fr),
    column-gutter: 20pt,
    [
      #text(size: 7.5pt, weight: "bold", fill: muted)[#upper(labels.at("payment"))]
      #v(3pt)
      #if employer.at("bank", default: "") != "" { [#employer.bank] } else { [#labels.at("bank_transfer")] }
    ],
    [
      #text(size: 7.5pt, weight: "bold", fill: muted)[#upper(labels.at("notes"))]
      #v(3pt)
      #if p.notes != "" { [#p.notes] } else { [#labels.at("retain_copy")] }
    ],
  )
}
