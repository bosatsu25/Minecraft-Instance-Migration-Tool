# ADR 0003: One filesystem kernel, planning-time Replace kind rule, and supply-chain hardening

- Status: Accepted
- Date: 2026-10-27
- Review report: [2026-10 architecture, security, and distribution review](../review/2026-10-architecture-security-review.ja.md)

## Context

A full review of v1.0.0 found that the technology stack was sound for the product, but that the
security-critical Windows filesystem code had been copied rather than shared:

- `IsSingleName` existed four times, local-path normalization four times, the held directory chain
  three times, the NT interop declarations three times, overlap checks five times, the tree
  fingerprint algorithm twice, and the checksummed JSON Lines journal mechanism twice;
- the copies had already drifted: backup root opening did not reject a non-directory component,
  the Inspector did not guard its drive classification, and only the rollback-attempt journal parsed
  records strictly;
- every adapter repeated an eight-branch exception ladder.

The review also found that a Replace of a destination whose kind differs from the catalog
(for example `config` that is a file) passed planning, Preview, and Backup, but the executor always
refused it after the durable `Started` record, so the run ended as `RecoveryRequired` although no data
changed.

CI used floating action tags, persisted checkout credentials, expanded job outputs into scripts,
exposed signing secrets to later steps through `GITHUB_ENV`, had no NuGet lock files, and let a
`pull_request_target` workflow send fork pull-request content to an external model endpoint.

## Decision

1. `Infrastructure/FileSystem` is the single kernel:
   `NtFileApi` (all P/Invoke), `WindowsPathRules` (one lexical path policy, testable on any OS),
   `FileSystemErrors` (one NTSTATUS/exception classification), and `ChecksummedJournal`
   (create-only no-follow files, strict bounded reads, torn-tail handling, checksums, flushed appends).
   `WindowsExecutionTree` is the only tree traversal, copy, delete, and fingerprint implementation.
   Adapters keep only their port-specific failure mapping.
2. The fingerprint encoding is a persisted contract. Golden-value tests, computed independently from
   the documented encoding, pin it so that refactoring cannot invalidate journals or backups written
   by earlier releases. Strict JSON reading changes no serialized bytes.
3. Planning refuses a Replace whose destination kind differs from the catalog
   (`BlockedDestinationKindMismatch`) before Backup or execution; Skip remains allowed.
4. Each directory handle requests only the rights its operation needs (for example, journal parents
   receive add-file rights but not delete-child rights). P/Invoke resolution is restricted to System32.
5. Supply chain: actions are pinned to commit SHAs with `persist-credentials: false`; job outputs reach
   scripts only through the environment; signing secrets are scoped to the signing step; test projects
   restore with lock files in locked mode against an explicit `nuget.org` source mapping; Dependabot
   proposes updates; tag releases receive GitHub build-provenance attestations; the external review
   workflow ignores fork pull requests.

## Technology kept, and why

| Choice | Decision | Reason | Alternatives rejected |
| --- | --- | --- | --- |
| .NET 10 LTS, self-contained | Keep | Members install no runtime; LTS support window | Framework-dependent shifts a runtime install to members |
| WPF | Keep | Windows-only product, mature, no extra runtime | WinUI 3 adds Windows App SDK deployment; Avalonia adds a third-party UI stack without a cross-platform need |
| Untrimmed, non-single-file folder | Keep | Trimming is unsupported for WPF; single-file must extract WPF native libraries to a temporary directory | Single-file / trimming / NativeAOT |
| Inno Setup per-user installer + ZIP | Keep | No administrator rights; CI proves install, reinstall, and residue-free uninstall | MSIX requires a trusted signature; WiX adds tooling without an MSI requirement |
| Handle-relative NT APIs | Keep, consolidated | Structurally prevents following junctions during recursive delete/copy and TOCTOU path swaps | `System.IO` recursive APIs resolve paths by string |
| `DllImport` | Keep | Proven by Windows integration tests; `LibraryImport` would need unsafe code and new marshalling for no AOT/trim benefit here | `LibraryImport` |

## Consequences

- Infrastructure shrinks from 6,747 to 4,891 lines (-27%); a fix to path, handle, or journal rules
  now applies everywhere.
- Some failure categories become more precise in rare races (for example, a non-directory backup
  parent is `InvalidPath` instead of an IO failure). All remain fail-closed.
- Journals with unknown, duplicate, or missing members are rejected. Journals written by v1.0.0
  contain none of these and still load.
- Infrastructure and App tests still require Windows; lexical path rules and planning rules are
  verifiable on any platform.
- The largest remaining user burden is the unsigned distribution (SmartScreen / Unknown Publisher).
  Attestations prove build provenance to distributors but do not change SmartScreen.
