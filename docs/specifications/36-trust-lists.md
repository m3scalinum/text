# 36 — European trust lists

## 1. Scope

`M3scalinum.Text.Pdf` gains one reader and one question:

- `PdfLotl` — `Read`, for a trust list in the ETSI TS 119 612 format.
- `PdfTrustedList` — a list: its territory, its edition, its services, the other lists it points at,
  and the lookups that answer "which service covers this certificate, then?".
- `PdfTrustService` — one service: its type, its status history, its certificates, whether the list
  marks it qualified.
- `PdfTrustServicePeriod` — one status and the window it held.
- `PdfTrustListPointer` — a pointer to a national list.
- `PdfSignatureTrustCoverage`, `PdfTrustListVerification` — what a list says about the signatures of
  one document, and what the verdict rests on (`PdfQualificationBasis`).
- `PdfTrustListSignature`, `PdfTrustListSignatureStatus` — what the list's own XML-DSig proved: its
  signer, the references it covers, the signed time, and whether the signer chained to the anchors the
  caller supplied (§7.1).
- `PdfTrustListException`, `PdfTrustListError` — why a list was refused.

**In scope.** Reading the XML, the model it describes, the certificate-to-service match, the list's own
signature checked offline against caller-supplied anchors, and the statement the verifier makes about
each signature once a list is supplied.

**Out of scope, deliberately.**

| Not done | Why |
| --- | --- |
| Downloading | Not one socket. The caller fetches, caches and refreshes; a verification that opened a connection to decide a document could be steered by whoever controls the DNS. |
| Verifying the list's *trust chain* for it | The list's own XML-DSig is verified here — §7.1 — against the anchors the caller supplies (`PdfTrustStore.ForNationalList`). What the package does not do is decide that those anchors are themselves trustworthy, or follow the LOTL to fetch a list; the caller picks the anchors and fetches the bytes. |
| Following `OtherTSLPointer` | A LOTL is read into `NationalLists`; loading a national list is the caller's call, so a list-of-lists never cascades into a network walk. |
| Checking freshness | `IssueDateTime`, `NextUpdate` and `IsExpiredAt` are exposed. Enforcing them is a policy decision, not a parse. |
| Inferring qualification from the issuer | A certificate issued by a qualified CA is not itself stated to be qualified by the list. §4 says what matches, and nothing else matches. |
| Revocation | A trust list says a service was granted; it never says a certificate was not revoked. `PdfDss` and `IRevocationSource` are the revocation seam. |

## 2. The format read

The reader accepts a `TrustServiceStatusList` document — the LOTL published by the European
Commission and the national lists it points at, in the v2 namespace or v1. Elements are matched by
**local name**, so both namespace versions read the same, and a document is never trusted more
because it declares a namespace.

| Read | Where | Into |
| --- | --- | --- |
| `SchemeTerritory` | `SchemeInformation` (required) | `PdfTrustedList.Territory` |
| `TSLVersionIdentifier` | `SchemeInformation` | `Version` |
| `TSLSequenceNumber` | `SchemeInformation` | `SequenceNumber` |
| `ListIssueDateTime` | `SchemeInformation` (required) | `IssueDateTime` |
| `NextUpdate/dateTime` | `SchemeInformation` | `NextUpdate` |
| `TSLType` | `SchemeInformation` | `ListType`, `IsListOfLists` |
| `OtherTSLPointer` | `PointersToOtherTSL` | `NationalLists` |
| `TSLLocation` | each pointer (required) | `PdfTrustListPointer.Location` |
| `OtherInformation/SchemeTerritory` | each pointer | `PdfTrustListPointer.Territory` |
| `OtherInformation/SchemeOperatorName` | each pointer | `PdfTrustListPointer.OperatorName` |
| `ServiceDigitalIdentities/X509Certificate` | each pointer | `PdfTrustListPointer.Certificates` |
| `ServiceTypeIdentifier` | `ServiceInformation` (required) | `PdfTrustService.Type`, `TypeIdentifier` |
| `ServiceName` | `ServiceInformation` | `PdfTrustService.Name` |
| `ServiceStatus` | `ServiceInformation` (required) | `PdfTrustService.Status`, `StatusIdentifier` |
| `StatusStartingTime` | `ServiceInformation` (required) | the current period's start |
| `ServiceHistoryInstance/ServiceStatus`, `StatusStartingTime` | `ServiceHistory` | the earlier periods |
| `ServiceDigitalIdentity` | `ServiceInformation` | `PdfTrustService.Certificates` |
| `Qualifier[@URI]` | `ServiceInformation` | `PdfTrustService.IsQualified` |
| `TSPName` | `TSPInformation` | `PdfTrustService.ProviderName` |

**Ignored, on purpose:** `SchemeOperatorName` of the list itself, `SchemeInformationURI`,
`SchemeTypeCommunityRules`, `SchemeTerritory` of a pointer beyond the one field above, `MimeType`,
`ServiceSupplyPoints`, `TSPServiceDefinitionURI`, `SchemeServiceDefinitionURI`, the `KeyValue` and
`X509SubjectName` forms of a digital identity, `AdditionalServiceInformation`, `Extension` elements
other than their `Qualifier` children, and every signature element (`Signature`,
`SignatureMethod`, …). Each one is either metadata a caller never decides with, or a form of identity
this reader cannot match a certificate against.

The types a service is classified into are `CA/QC`, `CA`, `AdESig`/`AdESigQC`, `AdESeal`/`AdESealQC`,
`TSA`; anything else is `PdfTrustServiceType.Other` with the raw URI kept in
`TypeIdentifier`. Statuses are `granted` and `accredited` (both `Granted`), `withdrawn`, and
`deprecated`; anything else is `Unknown` — and an unknown status never covers a signature, because
`Covers` asks for `Granted` explicitly.

A certificate whose base64 does not decode, or whose bytes are not a certificate, is dropped rather
than failing the list: the service is still reported, and it covers nothing by that entry. A service
that names no certificate at all is read the same way — present, covering nothing.

## 3. The model

A list states statuses as a history: it writes the moment the current status started, and the
moments the previous ones did. The end of a status is therefore the start of the next, and the
current one has no end. `PdfTrustService.Periods` is that reading, oldest first, the current period
last:

```text
ServiceHistoryInstance  granted    2022-01-01
ServiceStatus           withdrawn  2024-01-01
    → [granted 2022-01-01 → 2024-01-01], [withdrawn 2024-01-01 → ∞]
```

`ValidFrom` is the current period's start and `ValidTo` is null for every service a list publishes:
the format does not know when a granted status will end. A service is relied on at a moment only when
the period covering that moment is `Granted`; a certificate that was qualified in 2023 is not
qualified for a signature claimed in 2025 because the service was withdrawn in between.

`Location` is not a field of the format: a list does not carry its own URL, so `Read` takes it as
an optional argument and records it, and nothing fetches it.

A list of lists carries pointers and no service of its own. `IsListOfLists` reports the `TSLType`,
and each pointer exposes the territory, the operator, the URL and the certificate the LOTL binds to
that national list's signature.

## 4. Matching a certificate to a service

The match is exact and narrow:

1. The service's digital identity must contain the **same DER certificate** — compared byte for byte,
   with `SHA-256` over the DER bytes as the alternative index. Nothing matches by subject name, by
   issuer name, by serial number or by public key: two certificates that share a subject are two
   certificates.
2. A granted period must cover the instant asked about — the signing time, not the moment of the
   call.
3. Every service satisfying both is returned, in document order. Overlapping windows are normal (a
   renewal, two providers, a status change), so the API returns a list and the caller decides; the
   single-value `FindService` is the first of that list.

The signing certificate is matched, and nothing else. In a real list the entry that names a signer's
certificate is usually the issuing CA's `CA/QC` entry, not the signer's certificate: a qualified CA
issues both qualified and unqualified certificates, and the list says nothing about which is which.
Treating "issued by a service the list marks qualified" as "qualified signature" would therefore
invent a fact. A caller that wants that inference makes it, and says so in its own policy.

## 5. How the verifier uses the list

`PdfSignatureVerifier.Verify(pdf, trustStore, trustList)` is the entry point. It runs the same
verification as the two-argument overload and adds one `PdfSignatureTrustCoverage` per signature:

| Signature | List | Coverage |
| --- | --- | --- |
| intact | `null` | empty — no list was supplied |
| intact | certificate listed, service `QC`, period covering the signing time | `IsQualified` true, `Service` set |
| intact | certificate listed, service not marked `QC` | `IsQualified` false, `Service` set, explanation names the type |
| intact | certificate absent | `IsQualified` false, `Service` null, "not found in any granted service" |
| broken | anything | the coverage is computed from the certificate the CMS still carries, and the signature stays `IsIntact` false |

The instant judged is `TimestampedSigningTime` when the verifier established one and
`ClaimedSigningTime` otherwise — a claim, which the explanation says in words, because a claim
checked against a list proves at most that the two agree. `EvaluatedAt` is exposed so a caller can
print the date the answer is about.

A certificate the list does not know is **not** a verification failure. The two answers are separate
facts and the result type keeps them apart: `PdfTrustListVerification.Signatures` is what the
verifier checked about the bytes and the chain, `Coverage` is what a territory published. A caller
that requires qualification asks `AllQualified` and says so in its own policy.

## 6. The XML policy

A trust list is XML written by a third party, so it is read under the policy the suite applies to
every XML part ([22 §10](22-docx.md#10-the-xml-policy)), not under a weaker one because it comes
from an institution. [29](29-threat-model.md) notes that `PdfAXmp` still parses XMP with
`XDocument.Parse` and no bound; this reader is not allowed to repeat that.

**Decision — one door, again.** `PdfLotl` creates `XmlReaderSettings` in one function and nowhere
else in the package. `DtdProcessing = Prohibit`, `XmlResolver = null`, `MaxCharactersInDocument` at
the byte cap, `MaxCharactersFromEntities` at 1 MiB. The document type declaration is also refused
before the parser sees it, by scanning the bytes for `<!DOCTYPE` or `<!ENTITY`: the scan is coarser
than a real classification and is meant to be — a document that merely mentions the syntax is
refused too, and a trust list's names never contain it. The reader's own prohibition remains the
backstop for a declaration the scan cannot see in another encoding.

| Rule | Bound | Refused as |
| --- | --- | --- |
| Document type declaration | none permitted | `PdfTrustListError.XmlPolicy` |
| Entity declaration or external entity | none permitted | `PdfTrustListError.XmlPolicy` |
| Entity expansion | 1 MiB | `PdfTrustListError.XmlPolicy` |
| One list | 8 MiB (`PdfLotl.MaxXmlBytes`) | `PdfTrustListError.Limit` |
| Element nesting | 64 levels (`PdfLotl.MaxXmlDepth`) | `PdfTrustListError.Limit` |
| Elements in one list | 1 000 000 (`PdfLotl.MaxXmlElements`) | `PdfTrustListError.Limit` |
| Services | 100 000 (`PdfLotl.MaxServices`) | `PdfTrustListError.Limit` |
| Pointers to other lists | 4 096 (`PdfLotl.MaxPointers`) | `PdfTrustListError.Limit` |
| Certificates per service or pointer | 64 (`PdfLotl.MaxCertificates`) | `PdfTrustListError.Limit` |

The bytes are parsed twice: once to walk the token stream and enforce the depth and element bounds,
once to build the tree that the model is read from. The tree is only built after the walk passed, so
a deeply nested document is refused before a builder can recurse into it — and a `Stream` overload
copies at most `MaxXmlBytes` + 1 bytes, so an endless stream stops at the cap rather than growing
the heap.

## 7. What a trust list proves, and what it does not

**It proves**, when a coverage entry is qualified: that the territory named by `Territory` published
a list, in which a service marked qualified named this exact certificate, and that the service's
granted period covers the moment judged. When the list was read with anchors, it also proves that the
bytes are the ones the signer signed, and `Basis` says whether that signature was verified and whether
the list was still current. That is a statement about the certificate and the date. Nothing more.

**It does not prove** any of the following, and no reading of this API should be taken to:

- That the list is genuine **when it was read without anchors**. `PdfLotl.Read(bytes)` parses the bytes
  it is given; read that way, a hostile list that says a certificate is qualified is read as a list
  that says so, and the coverage it produces reports `Basis = NotChecked`. Read it with anchors —
  `PdfLotl.Read(bytes, null, PdfTrustStore.ForNationalList(lotl, "FR"))` — and §7.1 applies instead.
- That the list came from the LOTL, or that its sequence number is higher than the one seen
  yesterday. Those are the caller's comparisons; the signature cannot make them.
- That the list is current. `IsExpiredAt` answers the question and the coverage names the basis
  (`Verified` or `VerifiedExpired`); nothing refuses a stale list on its own.
- That the signer was the certificate's holder, or that the private key lived in a qualified device.
  `QCWithSSCD` and friends are read only as far as `IsQualified`; the qualifiers themselves are not
  interpreted.
- That the certificate's chain is trusted, or that it was not revoked. `PdfChainStatus` and the
  revocation seam answer those, separately.
- That the signature is valid. `PdfSignatureInfo` answers that.

### 7.1 Verifying the list itself

`PdfLotl.VerifySignature(xml, anchors)` checks the list's own XML-DSig, offline, and
`PdfLotl.Read(xml, location, anchors)` attaches the same verdict to the list it returns
(`PdfTrustedList.Signature`):

```csharp
var lotl = PdfLotl.Read(lotlBytes);
var national = PdfLotl.Read(nationalBytes, "https://example.test/FR.xml",
                            PdfTrustStore.ForNationalList(lotl, "FR"));

// national.Signature.Status        — Valid, Invalid or NoSignature
// national.Signature.SignerChain   — Trusted against the LOTL's anchors, Untrusted, or NotEvaluated
// national.Signature.IsVerified    — both of the above, plus a reference that covers the document
```

**What is verified.** The signature is an enveloped XML-DSig, usually inside an XAdES envelope, and the
check covers it as such:

| Checked | How |
| --- | --- |
| Reference digests | every `ds:Reference` is resolved — `URI=""` is the whole document, `#id` is the element carrying that `Id` — its transforms are applied (the enveloped-signature transform removes the `ds:Signature` element, then canonicalisation) and its `DigestValue` is compared |
| `SignatureValue` | verified over the canonical form of `ds:SignedInfo` with the certificate in `ds:KeyInfo/X509Data` |
| Canonicalisation | Canonical XML 1.0 and exclusive C14N, both without comments; any other transform or canonicalisation algorithm is refused **by name**, never guessed at |
| Algorithms | RSA (SHA-1, SHA-256, SHA-384, SHA-512) and ECDSA digests and signatures |
| Signer chain | built against the anchors the caller supplied, at `xades:SigningTime` when the list states one, with downloads disabled |
| Document coverage | at least one verified reference must cover the whole document; a signature over a detached element alone is `Valid` but not `IsVerified` |

One byte changed anywhere the references cover makes the digests disagree, and the verdict says which
reference failed. `SignedAt` reads `xades:SigningTime` — it is inside the signed bytes, so it is as
trustworthy as the signature and no more.

**What is not verified.** This is a statement about bytes, not a full AdES validation, and the list's
authority rests only on the anchors the caller named:

- No timestamp token over the signature, so `SigningTime` is read, never proven. An XAdES-T or -LT
  level is not established.
- No check of the XAdES `SignedProperties` beyond digesting the ones a reference names — `SigningCertificate`,
  `SignaturePolicyIdentifier` and the rest are carried, not interpreted.
- No revocation of the signer, and no certificate policy or path-length policy beyond what
  `X509Chain` applies for the anchors given.
- No sequence-number or freshness enforcement (§7).
- Two canonicalisation algorithms are implemented, so a list signed under another transform is
  refused rather than accepted on an assumption. A refusal is never evidence of forgery: it says this
  reader could not check those bytes.

**What the qualification verdict says it rests on.** `PdfSignatureTrustCoverage.Basis` makes the
distinction the caller has to be able to see:

| `Basis` | Meaning |
| --- | --- |
| `Verified` | the list's signature verified against a supplied anchor and the list is inside its `NextUpdate` |
| `VerifiedExpired` | the signature verified, and the list is past `NextUpdate`; `IsQualified` still reflects the service, and the explanation names the expiry |
| `NotVerified` | a verdict was attached and the bytes are not proven — **no verdict says "qualified"**, whatever the services claim |
| `NotChecked` | the list was read without anchors; the qualification is the list's statement, and the explanation says so |

Once a document is signed and a list is trusted, `PdfSignatureVerifier` answers both halves:

```csharp
var result = PdfSignatureVerifier.Verify(pdf, store, national);

foreach (var coverage in result.Coverage)
{
    // coverage.Basis says whether the list itself was verified; coverage.IsQualified says what it states.
}
```

## 8. Decisions recorded

- **No cascade.** Reading a LOTL does not read its national lists. A cascade would make one call
  perform thirty network requests under a policy the caller never chose, and would make a
  verification's cost depend on a document's content.
- **No cache.** The list is immutable data the caller holds. A cache inside the library would need
  an eviction policy, a clock and a failure mode the caller cannot see.
- **Exact certificate match, not issuer inference.** §4.
- **Statuses as periods, not a boolean.** A withdrawn service still explains a 2023 signature; a
  model that collapsed status to "granted or not" would answer the wrong question about the past.
- **A list is read whole or refused.** A missing `ServiceInformation`, `ServiceTypeIdentifier`,
  `ServiceStatus` or `StatusStartingTime` is `Malformed`, never skipped: a service silently dropped
  from the model is a certificate silently declared unqualified. A single undecodable certificate is
  the exception, because a certificate this reader cannot hold still leaves the service's identity
  readable.

## 9. Tests

`tests/Text.Pdf.Tests/PdfLotlTests.cs`, one assertion per fact:

- `A_minimal_list_is_read_with_its_territory_edition_and_one_service`
- `Several_services_with_overlapping_periods_are_all_reported`
- `A_withdrawn_service_is_read_but_never_covers_a_later_signature`
- `A_certificate_is_found_by_its_sha256_thumbprint`
- `A_certificate_absent_from_the_list_is_reported_as_not_found`
- `A_service_that_names_no_certificate_is_read_and_covers_nothing`
- `A_document_type_declaration_is_refused_as_an_xml_policy_breach`
- `An_external_entity_is_refused_as_an_xml_policy_breach`
- `A_list_nested_past_the_depth_cap_is_refused`
- `A_list_over_the_size_cap_is_refused_before_it_is_scanned`
- `Bytes_that_are_not_a_trust_list_are_refused_as_malformed`
- `A_list_reads_the_same_from_a_stream`
- `A_pointer_to_a_national_list_is_exposed_and_never_followed`
- `A_signature_covered_by_a_qualified_service_is_reported_as_qualified`
- `A_signature_whose_signer_is_absent_from_the_list_stays_intact_and_is_not_qualified`
- `A_service_the_list_does_not_mark_qualified_covers_the_signer_without_qualifying_it`
- `A_verification_without_a_list_carries_no_coverage`
- `A_qualified_ca_certificate_in_the_store_still_judges_the_chain`

The fixtures are hand-written ETSI documents carrying certificates the suite generated, so a match
is a match of DER bytes. The signing tests reuse `TestCertificates` and `PdfSigner`: no second
signing infrastructure was invented for them.

The list's own signature is covered by `tests/Text.Pdf.Tests/TrustListSignatureTests.cs`, one
assertion per fact, with documents the test writes and signs in memory:

- `A_signed_list_whose_signature_verifies_against_the_anchors_is_reported_verified`
- `A_list_whose_signed_bytes_changed_by_one_byte_is_refused_with_the_reason`
- `A_list_signed_by_a_certificate_the_anchors_do_not_cover_is_not_verified`
- `A_list_verified_without_anchors_is_not_verified`
- `A_signature_that_covers_only_a_detached_element_is_not_verified`
- `A_list_without_a_signature_is_reported_unsigned`
- `An_unsupported_canonicalisation_is_refused_by_name`
- `An_unsupported_signature_method_is_refused_by_name`
- `A_reference_to_an_identifier_the_document_does_not_carry_is_refused`
- `A_list_signed_with_sha1_is_verified`, `A_list_signed_with_sha384_is_verified`,
  `A_list_signed_with_sha512_is_verified`
- `A_national_list_is_verified_with_the_anchors_the_lotl_publishes`
- `A_qualification_on_a_list_whose_signature_does_not_hold_is_never_qualified`
- `A_qualification_on_a_verified_current_list_names_the_verification`
- `A_qualification_on_a_verified_list_past_its_next_update_says_the_list_expired`
- `A_list_read_without_anchors_reports_the_qualification_as_unchecked`
- `The_canonical_form_sorts_attributes_escapes_text_and_keeps_carriage_returns` — the canonical form
  is pinned against a literal, so the rules are checked rather than assumed to match the signer.

The fixtures are signed through the package's own canonicalisation: the digest covers the bytes the
verifier will canonicalise, so a change to one is a change to the other, and the tampering tests prove
the digest path notices. What that arrangement cannot prove is that a *third-party* canonicaliser
agrees with this one — §7.1 says what is implemented and that anything else is refused.
