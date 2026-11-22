# 33 — Batch integrity manifests

## 1. Scope

`M3scalinum.Text.Pdf` gains one entry point that seals a batch of files as a manifest of fingerprints
and checks a batch against one:

- `PdfBatchManifest` — `Build`, `Load`, `Verify`.
- `PdfBatchManifestDocument` — a sealed manifest: its entries, its seal, its canonical bytes.
- `PdfBatchManifestEntry` — one file: identifier, size, SHA-256, addition time, last-write time.
- `PdfBatchVerification` — the outcome of a check: one `PdfBatchEntryResult` per name, the breaks.
- `PdfBatchEntryState` — `Current`, `Modified`, `Missing`, `Added`.
- `PdfBatchVerificationMode` — `Full` or `Fast`.

Like the rest of the package it is bytes-in, bytes-out, except that its input is a directory tree: it
opens files to hash them and writes nothing.

**In scope.** The per-file fingerprint, the canonical text form of the manifest, the seal over that
form, and the four states a verification reports.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| Signing the manifest | A manifest is proof of integrity, not of authorship. §6 says how a caller gets the stronger proof. |
| Storing the files | A thousand sealed invoices cost a thousand hashes, not a thousand copies. The manifest holds no content. |
| Repairing a batch | Verification reports; the caller decides what to do with a modified file. |
| Watching a batch | There is no filesystem watcher, no cache and no service. Every call reads the disk. |
| Reading PDF structure | A batch is a directory of files. Whether a piece is a valid PDF is `PdfConformance`'s question, not this one's. |

## 2. The batch and its identifiers

A batch is a directory. `Build` walks it recursively and takes **every regular file** under the root:
no extension filter, because a lot of invoices may carry an XML or a `.p7m` beside each PDF, and a
silent filter would let an unsealed file sit in the archive.

An entry's identifier is the file's path **relative to the root**, with `/` separators whatever the
platform writes. That is what makes two files named `invoice.pdf` in two sub-directories two entries:

```text
2026-01/invoice.pdf
2026-02/invoice.pdf
```

Nothing is compared by bare name, and nothing joins on one. Identifiers are compared **ordinally** —
on Windows two names that differ only in case cannot coexist in one directory anyway, and on a
case-sensitive filesystem they are two files, which is what ordinal comparison reports.

The absolute root is not part of the manifest: the batch can be copied to another machine, or
archived under another path, and still verify.

A manifest written **inside** the batch root is a file of the batch: the next `Build` seals the
manifest, and the next verification reports it as added. Store it beside the root, not in it.

## 3. The format

The manifest is text, versioned, line-oriented and readable. Its canonical form is:

```text
M3SCALINUM-BATCH-MANIFEST 1
entry<TAB>SHA-256<TAB>SIZE<TAB>LAST-WRITE<TAB>ADDED<TAB>ID
entry<TAB>SHA-256<TAB>SIZE<TAB>LAST-WRITE<TAB>ADDED<TAB>ID
seal<TAB>SHA-256
```

**MUST**, byte for byte:

- **Encoding** — UTF-8 without a byte-order mark. A manifest that is not valid UTF-8 is refused.
- **Line endings** — one line feed (`U+000A`) after every line, including the last. No carriage
  return, no missing final newline.
- **Header** — the first line is exactly `M3SCALINUM-BATCH-MANIFEST 1`. The trailing number is the
  format version; a reader refuses a version it does not know rather than guessing.
- **Entry line** — six fields separated by one tab, in this order:
  1. the literal `entry`;
  2. the file's SHA-256, 64 lowercase hexadecimal digits;
  3. the file's length in bytes, invariant decimal, no sign, no padding;
  4. the file's last-write time at seal time, UTC;
  5. the time the manifest was sealed, UTC — the entry's addition time;
  6. the identifier.
- **Timestamps** — `yyyy-MM-ddTHH:mm:ss.fffffffZ`: UTC, exactly seven fractional digits, `Z`. A
  timestamp that does not parse in that exact shape is refused.
- **Identifiers** — relative paths with `/`, sorted ascending by ordinal comparison, no duplicates.
  The four characters that would break a line — `%`, tab, line feed, carriage return — are
  percent-escaped (`%25`, `%09`, `%0A`, `%0D`). Every other character, non-ASCII included, is written
  as it is.
- **Seal line** — the last line: the literal `seal`, one tab, the seal, one line feed.
- **Seal** — the SHA-256, lowercase hexadecimal, of **exactly the bytes from offset zero up to and
  including the line feed that ends the last entry line** — that is, everything before the seal line.
  The header is covered too.

**Why a fixed separator and not JSON.** JSON would need a decision per name — escaping, key order,
number formatting, a canonical serializer — and each of those is a way two constructions of the same
batch could disagree. A tab-separated line has one spelling per field, and the seal is a hash of a
string the specification writes out. The format is still a text a human can read and diff.

**Decision — the seal covers the entries, the entries do not cover the seal.** A byte changed
anywhere in an entry line changes the recomputed hash and the seal line no longer matches. A byte
changed in the seal line itself no longer matches the entries. Either way `Load` refuses the
manifest, and one message names the seal and says that one byte changed.

`PdfBatchManifestDocument.ToBytes` returns these bytes; `PdfBatchManifest.Load` takes them apart and
checks the seal. Loading a manifest that was written by `ToBytes` returns the same entries, the same
seal and the same bytes — that round trip is what §7 asserts.

## 4. Building and loading

```csharp
var manifest = PdfBatchManifest.Build("/archives/2026-11");
File.WriteAllBytes("/archives/2026-11.manifest", manifest.ToBytes());

var sealedManifest = PdfBatchManifest.Load(File.ReadAllBytes("/archives/2026-11.manifest"));
```

`Build(root, addedAt)` hashes every file once and takes its length and last-write time. `addedAt` is
the instant recorded as every entry's addition time; it defaults to the current time, and passing a
fixed value is what makes two constructions over the same files produce the same bytes, seal included.

`Load` refuses, with `PdfOperationException`:

- bytes that do not end in a line feed;
- a first line that is not the header this reader knows;
- a line that is not six tab-separated fields tagged `entry`;
- a fingerprint or a seal that is not lowercase SHA-256, a length that is not a decimal, a timestamp
  that is not the canonical shape;
- an identifier that is not a path inside the batch — absolute, containing `\`, `.` or `..`, or empty;
- entries out of ordinal order, or duplicated;
- a seal that does not match the entries.

The last refusal is the one one changed byte produces, and its message names the seal.

## 5. Verification

```csharp
var check = PdfBatchManifest.Verify("/archives/2026-11", sealedManifest);

foreach (var result in check.Entries)
{
    Console.WriteLine($"{result.Id}: {result.State}");
}
```

`Verify(root, manifest, mode)` returns one `PdfBatchEntryResult` per sealed name, in manifest order,
then one per file the root carries that nothing seals, in ordinal order of identifier. Each result is
one of four states:

| State | Meaning | `ExpectedSha256` | `ActualSha256` |
| --- | --- | --- | --- |
| `Current` | The file is the one that was sealed | sealed fingerprint | the file's, or `null` when a fast pass trusted length and time |
| `Modified` | Present, and its bytes are not the sealed ones | sealed fingerprint | the file's |
| `Missing` | Sealed, and the root no longer carries it | sealed fingerprint | `null` |
| `Added` | In the root, and the manifest does not seal it | `null` | the file's, or `null` in a fast pass |

`PdfBatchVerification.Breaks` holds one sentence per name that is not `Current` — the sentence names
the file, so a caller can report "invoice-2026-0042.pdf is modified" without walking the results.
`IsIntact` is `Breaks.Count == 0`. `Hashed` counts the files this pass actually read.

### 5.1 Full mode, and what it costs

`PdfBatchVerificationMode.Full` is the default. It hashes every file in the manifest and every added
file: **one read of the whole batch**. A thousand invoices of 300 kB is 300 MB read. That is the only
mode that can see a change that preserved the file's length and its last-write time — the attack a
filesystem timestamp cannot rule out.

### 5.2 Fast mode, and what it trusts

`PdfBatchVerificationMode.Fast` compares the file's length and last-write time with the entry first.
When both still match, the file is reported `Current` **without being read** (`WasHashed` is `false`),
so a batch where one invoice changed and nine hundred and ninety-nine did not costs one hash instead
of a thousand reads. When either moved, the file is hashed: a file whose content is unchanged but
whose timestamp was touched is still reported `Current`.

Fast mode's blind spot is stated rather than hidden: **a byte replaced inside a file of the same
length, with the last-write time put back, is not seen.** A caller that needs the guarantee runs
`Full`; a caller that runs a nightly `Fast` pass and a weekly `Full` one gets both the cheap check and
the strict one.

### 5.3 What verification does not do

`Verify` reads; it never writes, repairs or rewrites the manifest. A manifest whose seal does not
match is refused at `Load`, before any file is compared — verifying against a tampered manifest would
report the files as modified when the manifest is what changed. A root that is not a directory throws
`DirectoryNotFoundException` instead of reporting every file missing.

## 6. What the manifest proves, and what it does not

The manifest proves **integrity**: these files, with these bytes, were together when the seal was
computed, and none of them has changed since. It does not prove **authorship** — nothing in it is
signed, and anyone who can rewrite the files can rewrite the manifest and recompute its seal.

A caller that needs a proof it can show to a third party signs the manifest's bytes. Two shapes work:

- **Sign the manifest file itself.** It is plain bytes, so any detached CMS or XML signature over
  them is a proof of who sealed it and when. The signature travels beside the manifest.
- **Archive the manifest inside a PDF and sign that PDF.** An archived batch can carry its own
  proof:

```csharp
byte[] container = PdfAttachments.Add(pdf, "batch.manifest", sealedManifest.ToBytes(), "text/plain");
byte[] signed = PdfSigner.Sign(container, certificate);
```

Either way the signature covers the seal, and the seal covers the entries: a byte changed in a file,
or in the manifest, or in the signature, is visible. `PdfArchiveChain` then tells the caller whether
the signature over that container is still intact and still covers the whole revision.

What no manifest can prove is that a file **was** there when the seal was made: sealing records what
the disk held at that instant. A batch whose history matters needs the sealed manifest deposited
somewhere the producer cannot rewrite — which is what signing it and keeping it away from the batch
is for.

## 7. Tests

`tests/Text.Pdf.Tests/PdfBatchManifestTests.cs` writes a real temporary batch per test, so lengths,
timestamps and sub-directories are the filesystem's, not a mock's.

| Behaviour | Test |
| --- | --- |
| Three files, everything current | `A_batch_of_three_files_seals_a_manifest_that_verifies_current` |
| Entry fields and ordinal order | `The_entries_are_sorted_by_identifier_and_carry_size_fingerprint_and_times` |
| One byte changed in a file | `One_byte_changed_in_a_file_is_detected_and_the_file_is_named` |
| A sealed file removed | `A_file_removed_after_sealing_is_reported_missing` |
| A file added after sealing | `A_file_added_after_sealing_is_reported_added` |
| One byte changed in the manifest | `One_byte_changed_in_the_manifest_is_refused_naming_the_seal` |
| A seal line tampered with | `A_seal_line_tampered_with_is_refused` |
| Two homonyms in two sub-directories | `Two_files_with_the_same_name_in_two_sub_directories_are_two_entries` |
| Fast mode misses a same-length edit | `Fast_mode_trusts_size_and_timestamp_and_misses_a_same_length_edit` |
| Fast mode hashes a touched file | `Fast_mode_hashes_a_file_whose_timestamp_moved` |
| Canonical form is unique | `Two_constructions_of_the_same_batch_render_the_same_manifest` |
| Bytes round-trip through `Load` | `The_manifest_round_trips_through_its_canonical_bytes` |
| The documented layout, literally | `The_manifest_is_the_documented_canonical_text` |
| The manifest carries no content | `The_manifest_never_carries_the_files_it_seals` |
| A `%` in a name survives the round trip | `A_name_with_a_percent_sign_round_trips_through_the_manifest` |
| A forged manifest naming `../` is refused | `A_manifest_that_names_a_path_outside_the_batch_is_refused` |
| Bytes that are not a canonical manifest | `A_manifest_that_is_not_in_the_canonical_form_is_refused` |
| A root that does not exist | `A_root_that_is_not_a_directory_is_refused` |
