# 32 — Localized date and number formatting

## 1. Scope

`M3scalinum.Text.Documents` gains one pure formatter and the two values it takes:

- `TypstFormatting` — `FormatNumber`, `FormatCurrency`, `FormatDate`.
- `TypstNumberFormat` — the language and the number of fraction digits, built with `Plain` or
  `WithFractionDigits`.
- `TypstLanguage` — the languages the table has a row for: `English`, `French`.

**In scope.** Turning a `decimal`, a `DateOnly` and an ISO 4217 code into the text a reader of one
language expects: grouped thousands, the decimal separator, the side the currency symbol sits on,
and the default day/month/year order.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| Plurals, word order, articles | A figure is a token, not a sentence. Choosing between *un* and *une*, or ordering *net à payer*, is the label set's job (`TemplateLabels`). |
| Non-Gregorian calendars | `DateOnly` is a Gregorian date. A Hijri or Japanese-era rendering is a different value, not a different pattern. |
| Times of day, time zones | A document states a date; an instant is a caller's problem, and the suite takes a clock as an input where it needs one. |
| Per-currency minor units | Every amount prints two fraction digits. A table of ISO 4217 exponents (JPY 0, TND 3) is a currency reference, not a formatting rule, and is not shipped. |
| Grouping other than three digits | Every language in the table groups by three. A two-digit grouping (some Indian locales) would need a column in the table, not a guess. |
| Reading the process culture | The whole point: see §2. |

## 2. Why the process culture cannot be asked

`Directory.Build.props` sets `<InvariantGlobalization>true</InvariantGlobalization>` for the suite,
and the containers the suite runs in set it too. In that mode the runtime does not ship ICU data:
`CultureInfo.GetCultureInfo("fr-FR")` throws `CultureNotFoundException`, `new CultureInfo("fr-FR")`
succeeds but formats as the invariant culture, and a `DateTimeFormatInfo` for a named culture is
unreachable. A formatter written on top of those APIs therefore has two possible behaviours — throw,
or silently print the invariant answer — and neither is a French invoice.

What does work is a table in the code, applied by transforming separators, exactly as
`TemplateLabels` supplies the pack's words: the language is a parameter, not an ambient property of
the process.

**Decision — a table, not a `CultureInfo`.** Rejected: turning `InvariantGlobalization` off and
calling `ToString("N2", french)`. It would make the package depend on a runtime feature that is off
by contract, and it would make a document depend on the host's ICU build — a figure that renders as
`1,234.56` on one machine and `1 234,56` on another is not deterministic.

**Consequence — the only culture the code touches is the invariant one.** `ToString` is used for one
thing: decimal to digits (`"F2"`, `"D2"`, `"D4"` with `CultureInfo.InvariantCulture`). The invariant
culture exists in every mode, including `InvariantGlobalization`, and it is the same everywhere.
Every separator that reaches the reader comes from the table.

## 3. The table

One row per language, in `TypstFormatting`:

| Language | Thousands | Decimal | Symbol | Date |
| --- | --- | --- | --- | --- |
| `English` | `,` | `.` | before the number, no space — `€1,234.56` | month first — `03/01/2026` |
| `French` | U+00A0 | `,` | after the number, one U+00A0 — `1 234,56 €` | day first — `01/03/2026` |

The row also carries the date separator (`/` in both languages) and the day-first flag; there is no
pattern string to parse, so no pattern can be malformed. Adding a language is one `switch` arm and
one `TypstLanguage` member: there is no registry to register it in.

The language set is the set the template pack ships labels in. The two enums are separate —
`TemplateLanguage` lives in `M3scalinum.Text.Templates`, which depends on the document model and not
the other way round — but a document is written in one language, so the two sets are expected to
grow together.

`TemplateLabels` and this table do not disagree: the pack's `thousand_separator` is a plain U+0020
because it is interpolated into Typst source and read there by the layout's own `money` helper,
whereas `TypstFormatting` returns finished text for a `TypstContent`. See §4 for why the two
characters differ, and §8 for what that means for the pack.

## 4. The no-break space, and why it is U+00A0

French typography prefers U+202F, the narrow no-break space, for the thousands separator. **This
table uses U+00A0, for the thousands separator and between an amount and its symbol.**

Two reasons, the first of which is verifiable in the compiler source the suite embeds
(`typst-layout` 0.15.1):

- **The compiler special-cases U+00A0 and nothing else.** In `inline/shaping.rs`, `track_and_space`
  subtracts `nbsp_delta` from every glyph whose character is `'\u{00A0}'`, which normalises the
  advance of a no-break space to the width of an ordinary space, and `is_space` there treats `' '`,
  `'\u{00A0}'` and the ideographic space as spaces for justification. There is no such arm for
  U+202F: its width and its glyph are whatever the font provides, and a face without the character
  draws a missing glyph. A formatter cannot choose a character whose rendering depends on the font
  in use.
- **U+00A0 is what Typst itself produces.** `~` in markup compiles to U+00A0 (`typst-syntax`,
  `ast.rs`: `("~", '\u{00A0}')`), so the character is on the compiler's own path.

Both characters are non-breaking, so neither lets a figure break across a line; U+00A0 is simply the
one whose width the compiler controls.

**It does not disturb the markup.** In markup mode the lexer only makes a `Space` token out of
`' '`, `'\t'` and newlines (`typst-syntax`, `lexer.rs`, `is_space`: `SyntaxMode::Markup =>
matches!(character, ' ' | '\t') || is_newline(character)`). U+00A0 is therefore ordinary text: it
opens no block, starts no list, and needs no escape. `TypstMarkupWriter` escapes `~` but not U+00A0,
so a formatted figure reaches the compiler byte for byte — asserted by
`A_formatted_amount_reaches_the_markup_unchanged` and `A_formatted_date_reaches_the_markup_unchanged`.

## 5. The API

```csharp
string total = TypstFormatting.FormatNumber(1234.56m, TypstNumberFormat.Plain(TypstLanguage.French));
string quantity = TypstFormatting.FormatNumber(3m, TypstNumberFormat.WithFractionDigits(TypstLanguage.French, 0));
string amount = TypstFormatting.FormatCurrency(1234.56m, "EUR", TypstLanguage.French);
string issued = TypstFormatting.FormatDate(new DateOnly(2026, 3, 1), TypstLanguage.French);
```

`total` is `1 234,56`, `quantity` is `3`, `amount` is `1 234,56 €` and `issued` is `01/03/2026`.

The result is text, not a `TypstContent`: a caller writes it where it belongs —
`new TypstContent().Text(amount)` — and the markup writer escapes it like any other run. There is no
helper on `TypstContent` for the same reason `TypstLength` has none: formatting produces a string, and
the writer already owns turning a string into markup.

Rules the three methods obey:

- **Rounding is away from zero** at the last printed digit (`2.345` → `2,35`). A line and its total
  must not disagree because one rounded to even, and away-from-zero is what the commercial convention
  in the target languages does.
- **Zero prints its fraction digits** — `0,00` for an amount — and a value that rounds to zero carries
  no sign, so a credit of `-0.001` is not printed `-0,00`.
- **The integer part is grouped by three from the right**, and the first group is not padded.
- **An amount always has two fraction digits**, rounded like any number.
- **The currency symbol comes from a short table**: `EUR` → `€`, `USD` → `$`, `GBP` → `£`, `CHF` →
  `CHF`, `JPY` → `¥`, `CAD` → `CA$`, matched case-insensitively. A code the table does not know is
  printed as the caller wrote it — `1 234,56 xts` — because inventing a symbol for an unknown
  currency is worse than showing the code.

## 6. Dates

`FormatDate` assembles the date from three integers with the language's order and separator; there is
no format string and no `DateTimeFormatInfo`. `DateOnly` is used because the document model already
uses it (`TypstMetadata.Date`), and because a document states a date, not an instant.

The default pattern is the one business documents in each language print: `DD/MM/YYYY` in French,
`MM/DD/YYYY` in English. A caller that needs another pattern — an ISO `YYYY-MM-DD` on a machine
readable line — writes the digits itself; the formatter does not grow a pattern language.

## 7. An unknown language, an unknown currency

An undefined `TypstLanguage` — `(TypstLanguage)42` — throws `ArgumentOutOfRangeException`. There is no
silent fallback to the invariant style: the point of the type is that the language is stated, and a
document that asked for a language it cannot have is a bug in the caller, not a rendering choice. The
"closest invariant" behaviour is what the suite has today by accident, and it is exactly what this
specification removes.

An unknown ISO code is not an error: the caller may print a currency the table has no symbol for, and
the code itself is the honest rendering. An empty or whitespace code is an `ArgumentException`, because
there is nothing to print.

## 8. What this leaves to the template pack

The pack's layouts group and round inside Typst (`layouts/*/template.typ`, the `money` helper), driven
by the `thousand_separator` and `decimal_separator` labels. That path is unchanged by this
specification: a caller who renders through `TemplateCatalog` still gets the pack's own figures.

Two consequences, stated rather than hidden:

- The label separator is a plain U+0020, so in a justified table cell a French amount can break
  between groups. Passing U+00A0 as the label value is a one-line change to the pack, and belongs to
  the pack's specification (05), not to this one.
- A managed caller that formats with `TypstFormatting` and passes the labels to a layout gets figures
  from the label set, not from here. Unifying the two means letting a layout accept preformatted
  strings, which is a change to the layouts.

## 9. Tests

`tests/Text.Typst.Tests/LocalizedFormattingTests.cs` runs in the ordinary managed suite, with no
native library, under `InvariantGlobalization`.

| Behaviour | Test |
| --- | --- |
| `1234.56` in French | `French_numbers_group_with_a_no_break_space_and_a_comma` |
| `1234.56` in English | `English_numbers_group_with_a_comma_and_a_dot` |
| Negative, both languages | `A_negative_number_carries_one_leading_sign_and_no_double_separator` |
| Zero, and a value that rounds to it | `Zero_prints_its_declared_fraction_digits_and_never_a_sign` |
| A whole number, no fraction part | `A_whole_number_prints_without_a_fraction_part` |
| Grouping starts from the right | `A_four_digit_group_starts_the_first_separator_after_three_digits_from_the_right` |
| French amount, symbol after | `French_amounts_put_the_symbol_after_a_no_break_space` |
| English amount, symbol before | `English_amounts_put_the_symbol_before_the_number` |
| Unknown ISO code printed as written | `A_currency_code_the_table_does_not_know_is_printed_as_written` |
| ISO code matched case-insensitively | `A_currency_code_is_case_insensitive` |
| `2026-03-01` day first | `French_dates_are_day_first` |
| `2026-03-01` month first | `English_dates_are_month_first` |
| The difference comes from the language | `The_two_languages_render_the_same_value_differently` |
| Unknown language refused | `An_unknown_language_is_refused_rather_than_guessed` |
| Fraction digits bounded | `A_fraction_digit_count_outside_the_decimal_range_is_refused` |
| Rounding away from zero | `Rounding_goes_away_from_zero_at_the_last_printed_digit` |
| `CurrentCulture` changes nothing | `The_output_does_not_move_when_the_current_culture_is_reassigned` |
| No breakable space in an amount | `An_amount_contains_no_breakable_space` |
| Amount survives the markup writer | `A_formatted_amount_reaches_the_markup_unchanged` |
| Date survives the markup writer | `A_formatted_date_reaches_the_markup_unchanged` |

`The_output_does_not_move_when_the_current_culture_is_reassigned` is the weak form of the proof, and
the test says so: under `InvariantGlobalization` the only culture that exists is the invariant one, so
assigning `CultureInfo.CurrentCulture` cannot change how the runtime formats anything. What the test
establishes is that the answer is identical before, during and after the assignment; the strong proof
is structural — the call path has no access to a named culture, which is why §2 forbids one.
