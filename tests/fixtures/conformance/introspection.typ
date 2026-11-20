// Introspection — every item of the category, invoked once.
#metadata("note") <intro-note>

#context {
  let pages = counter(page).final()
  let headings = counter(heading).get()
  let figures = counter(figure).get()
  let notes = counter(footnote).get()
  let equations = counter(math.equation).get()
  let custom = counter("custom").get()

  let seen = state("seen", false)
  seen.update(true)
  let now = seen.get()
  let at = seen.at(here())
  let end = seen.final()

  let all = query(heading)
  let level1 = query(heading.where(level: 1))
  let figs = query(figure)
  let labelled = query(<intro-note>)

  [pages #pages, headings #headings, figures #figures, notes #notes,
   equations #equations, custom #custom, seen #now, at #at, end #end,
   all #all.len(), level1 #level1.len(), figs #figs.len(), labelled #labelled.len()]
}
