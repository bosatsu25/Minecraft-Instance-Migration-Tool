# Phase 0 foundation plan

**Goal:** Establish a buildable, verifiable Windows desktop repository without migration features.

**Architecture:** Four production projects with inward dependencies, three xUnit projects,
and a static WPF shell. No speculative domain types, service abstractions, or MVVM library.

**Technology:** C#, .NET 10 LTS, WPF, xUnit, Windows GitHub Actions.

**Design brief:** The Phase 0 request; permanent decisions are recorded in
[ADR 0001](adr/0001-technology-and-architecture.md).

## Implementation sequence

- [x] Create solution, SDK selection, shared compiler/format settings, and project references.
- [x] Add meaningful architecture tests for evaluated project references, UI independence,
  compiled dependencies, and Domain filesystem implementation dependencies.
- [x] Add the minimal WPF shell and Windows restore/build/test/format CI.
- [x] Document architecture, Graph Loop, future migration rules, testing, and development in English/Japanese.
- [x] Run restore, Release build, all tests, format verification, and diff/status review.
  Prove boundary tests reject a deliberately invalid dependency, then restore and re-verify.

## Constraints and review focus

- Keep all migration behavior out of Phase 0, including detection, planning, backup, and rollback.
- Domain references no other product layer; Application references only Domain;
  Infrastructure may reference Application/Domain; App is the composition root.
- Tests must detect unused project references, not only compiled references.
- Tests must use evaluated MSBuild properties so imported settings are included.
- Keep local SDK downloads, generated outputs, and machine-specific paths out of source control.
- Keep Git identity repository-local; never place credentials or identity configuration in tracked files.
- Human gates: merge, release, deployment, destructive remote operations, external data uploads,
  credentials, and irreversible history changes. No force push or remote history rewrite.

## Execution notes

The user supplied the design constraints and authorized local work. Phase 0.5 owns initial
repository bootstrap, the feature commit, push, PR creation, and hosted CI verification.

## Verification evidence

Verified locally on Windows using official .NET SDK 10.0.401, downloaded outside the
repository and checked against Microsoft's published SHA512 checksum.

| Command/check | Observed result |
| --- | --- |
| `dotnet restore` | Exit 0; seven projects restored |
| `dotnet build --configuration Release --no-restore` | Exit 0; zero warnings/errors |
| `dotnet test --configuration Release --no-build --no-restore` | Exit 0; 10 passed, 0 failed/skipped |
| `dotnet format --verify-no-changes --no-restore` | Exit 0 |
| `git diff --check` plus untracked-file diffs against an empty file | No whitespace errors |
| XML/JSON parsing and local Markdown links | Passed |

An unused Domain-to-Infrastructure project reference caused both evaluated-configuration
tests to fail; removing it restored the passing suite. Independent read-only review identified
missing path-taking StreamReader/StreamWriter constructor detection. That guard was fixed:
each path-based probe failed, both stream-based probes passed, then all probe code was removed
and the full build/test/format sequence passed again. No product migration code was retained.

## Phase 0.5 handoff

At Phase 0 handoff, the source tree had no commits, default branch, or remote; the working
files were untracked. Phase 0.5 establishes an empty `main` baseline, then carries these files
through a feature branch and pull request. The configured Windows CI result must be observed
before Phase 0.5 passes. Phase 1 remains gated on that hosted result and human PR review/merge.

The local verification used a temporary .NET SDK installation. Normal development requires
the selected SDK installed or on PATH. Rendered UI behavior and future migration
safety/compatibility remain outside Phase 0 verification; architecture guards do not exhaustively
detect indirect or native IO.
