// Layout — every item of the category, invoked once.
#set page(paper: "a4", margin: 2cm)

#align(center)[aligned]

#block(width: 50%)[block]

#box[box]

#place(right)[placed]

#pad(1em)[padded]

#grid(columns: 2, [a], [b])

#stack(dir: ltr)[stacked]

#columns(2)[in columns]

#hide[hidden]

#rotate(10deg)[rotated]

#scale(80%)[scaled]

#move(dx: 1em, dy: 1em)[moved]

#skew(ax: 5deg)[skewed]

#h(1em) #v(1em)

#context measure([measured])

#context {
  let l = here()
  let p = l.page()
  let pos = l.position()
  let loc = locate(<layout-label>)
  [page #p, x #pos.x, located #loc]
}

#pagebreak()

#colbreak()

#repeat[repeated]

#align(center)[labelled] <layout-label>
