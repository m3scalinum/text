<!--
  Keep the summary to what changed and why. The diff shows the how.
-->

## Summary

<!-- One paragraph. What behaviour changes, and for whom. -->

## Packages touched

<!-- Tick what this change reaches: it decides which version, which changelog and which CI run. -->

- [ ] `M3scalinum.Text.Typst` (and `.DependencyInjection`, `.Templates.Generator`)
- [ ] `M3scalinum.Text.Pdf`
- [ ] Build, CI, packaging or documentation only

## Related issue

<!-- "Closes #123" or "Refs #123". Write "none" if there is no issue. -->

## Type of change

- [ ] Bug fix (behaviour that was wrong)
- [ ] New feature (behaviour that did not exist)
- [ ] Breaking change (public API or output that consumers depend on)
- [ ] Documentation only
- [ ] Build, CI or packaging

## Checklist

- [ ] `cargo fmt --check` and `cargo clippy -- -D warnings` pass (if `native/` changed)
- [ ] `dotnet build -c Release` passes with no new warnings
- [ ] The unit suites of the packages touched pass:
      `tests/Text.Typst.Tests`, `tests/Text.Pdf.Tests`, `tests/Text.Mcp.Tests`,
      `tests/Text.Templates.Generator.Tests`
- [ ] Integration tests added or updated (if the native ABI or an exporter changed)
- [ ] `CHANGELOG.md` has an entry under `[Unreleased]`, and `CHANGELOG-Typst.md` or
      `CHANGELOG-Text.md` when a package's own surface changes
- [ ] Documentation updated: `wiki/<Package>-*.md` for user-facing behaviour, `docs/specifications/`
      for a contract
- [ ] Public API changes are reflected in the XML docs

## Platforms exercised

<!-- Tick what you actually ran, not what you expect to work. The suite packages need none of this:
     they are managed assemblies with no native asset. -->

- [ ] win-x64
- [ ] win-arm64
- [ ] linux-x64
- [ ] linux-arm64
- [ ] linux-musl-x64 (Alpine)
- [ ] osx

## Notes for the reviewer

<!-- Trade-offs, follow-up work, anything you are unsure about. -->
