// Bundle — the target in which `asset` and `document` are valid.
#asset("data.txt", "hello from the bundle")

#asset("nested/meta.json", "{\"count\": 2}")

#document("index.html")[
  = Bundled document

  The assets above are written next to this file.
]
