// Minimal template for the embedded-loading test.
//
// The host appends `#render((…))`, so this file only defines the function.

#let render(p) = {
  set document(title: p.title)
  set page(paper: "a4", margin: 2cm)
  set text(size: 10pt)

  heading(level: 1)[#p.title]
  par(lorem(30))
}
