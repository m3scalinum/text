# Security Policy

## Supported versions

M3scalinum.Text.Typst is pre-1.0. Security fixes land on the latest published version only; there are no
maintained release branches.

| Version | Supported |
| --- | --- |
| Latest published `0.15.1.<revision>` | Yes |
| Anything older | No |

## Reporting a vulnerability

**Do not open a public issue.** Report it privately through GitHub:

<https://github.com/m3scalinum/text/security/advisories/new>

Please include the package version, the platform, a minimal reproduction, and what an attacker gains.
You will get an acknowledgement, and a fix or a reasoned refusal, before any public disclosure. Credit
is given in the advisory unless you ask otherwise.

## Scope

M3scalinum.Text.Typst loads a native library into the host process and compiles documents that may come from
untrusted input. The following are in scope:

- **Memory safety in the C ABI.** A malformed or hostile input that causes a crash, a use-after-free
  or a buffer overrun in `libsharp_typst` or in the P/Invoke layer.
- **Sandbox escape.** A document that reads or writes a file outside the virtual world, reaches the
  network when `AllowDownload` is false, or executes anything on the host.
- **Resource exhaustion.** An input that defeats the configured limits (`MaxSourceBytes` and the
  other `TypstLimits`) and takes the process down.
- **Path traversal.** A virtual path in a world, a template or a package that resolves outside its
  root.
- **Package resolution.** A `@preview/...` package that resolves to something other than what the
  registry served, or a cache entry that can be poisoned.

## Out of scope

- Bugs in the Typst compiler itself that are reproducible with the upstream `typst` CLI. Report those
  to [typst/typst](https://github.com/typst/typst); we will pick up the fix with the next compiler
  bump.
- Denial of service from input you already control and chose to compile, when the limits are
  configured to allow it.
- Vulnerabilities in a dependency that has no exploitable path through M3scalinum.Text.Typst — report those
  upstream.

## Disclosure

The advisory is published once a fixed version is available. If a fix takes longer than 90 days, we
will agree on a disclosure date with you rather than leave the report open indefinitely.
