// Visualize — every item of the category, invoked once.
#image("pixel.png", width: 1cm)

#line(length: 100%, stroke: 1pt + red)

#rect(width: 2cm, height: 1cm, fill: rgb("#3366cc"), stroke: 1pt + black, radius: 2mm)

#square(size: 1cm, fill: blue)

#circle(radius: 1cm, fill: gradient.radial(white, navy))

#ellipse(width: 2cm, height: 1cm, fill: luma(0.8))

#polygon(fill: orange, stroke: none, (0pt, 0pt), (1cm, 0pt), (0pt, 1cm))

#curve(stroke: 1pt + green, curve.move((0pt, 0pt)), curve.line((1cm, 1cm)))

#rect(width: 2cm, height: 1cm, fill: gradient.linear(red, blue, angle: 90deg))

#rect(width: 2cm, height: 1cm, fill: tiling(size: (5pt, 5pt))[#rect(width: 2pt, height: 2pt, fill: red)])
