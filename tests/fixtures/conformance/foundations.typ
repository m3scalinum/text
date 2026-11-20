// Foundations — every item of the category, invoked once.
#let s = "text"
#let i = 42
#let f = 1.5
#let d = decimal("1.5")
#let b = true
#let a = (1, 2, 3)
#let dict = (name: "x", count: 2)
#let c = [content]
#let l = <foundations-label>
#let dt = datetime(year: 2026, month: 1, day: 1)
#let du = duration(seconds: 5)
#let by = bytes("hello")
#let p = path("a/b")
#let re = regex("a+")
#let sel = selector(heading)
#let t = type(s)
#let v = version(0, 15, 1)
#let au = auto
#let no = none
#let sy = sym.arrow.r
#let ca = calc.pow(2, 8)
#let ev = eval("1 + 1")
#let asr = assert(true)
#let rp = repr(s)
#let st = std
#let sy2 = sys
#let tg = target

#s #i #f #d #b #a.len() #dict.name #c #l #dt #du #by #p #re #sel #t #v #au #no #sy #ca #ev #asr #rp #st #sy2 #tg

// function, arguments, module
#let count(..args) = args.pos().len()
#let inc(x) = x + 1
#import "lib.typ" as m

#count(1, 2, 3) #inc(1) #m.greet("world")
