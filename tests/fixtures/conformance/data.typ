// Data loading — every item of the category, invoked once.
#let text = read("data.txt")
#let json = json("data.json")
#let yaml = yaml("data.yaml")
#let toml = toml("data.toml")
#let csv = csv("data.csv")
#let cbor = cbor("data.cbor")
#let xml = xml("data.xml")

#text

#json.items.len() items, #yaml.name, #toml.name, #csv.len() rows, #cbor.len(), #xml.len()
