# Peppol fixtures

These documents are the OpenPEPPOL publication's own examples, copied unmodified from
<https://github.com/OpenPEPPOL/peppol-bis-invoice-3>:

| File | Source |
| --- | --- |
| `base-example.xml` | `rules/examples/base-example.xml` |
| `base-creditnote-correction.xml` | `rules/examples/base-creditnote-correction.xml` |
| `Allowance-example.xml` | `rules/examples/Allowance-example.xml` |
| `direct-debit.xml` | `rules/snippets/direct-debit.xml` |

They are used as the positive cases of `PeppolConformityTests`: a document the publication itself
offers as an illustration of Peppol BIS Billing 3.0, and which the rule set in
`PeppolBisValidator` must accept. Do not edit them — a local edit would turn a published sample into
this repository's opinion. The documents that break a single rule are built from the model in the
test file instead.

The EAS code list and the rule identifiers embedded in `PeppolBisValidator` were read from
`rules/sch/PEPPOL-EN16931-UBL.sch` of the same publication.
