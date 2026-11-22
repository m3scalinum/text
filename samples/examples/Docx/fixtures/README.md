# Docx example fixtures

`report.docx` is a copy of `tests/Text.Docx.Tests/fixtures/report.docx`, the package under test.

It is a real `.docx` package assembled from raw OOXML parts (the test fixture records how), not a
file Word produced: every part the native reader reacts to is named in that fixture, so a failure
here points at the reader rather than at a producer's opinion.

The copy exists because an example must not read across the repository: the CI job copies this
directory into the generated project, and `DocxReader.Read` sees only bytes.
