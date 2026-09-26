# Testing and verification

## Verification commands

Run from the repository root on Windows using the SDK selected by `global.json`:

```powershell
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build --no-restore
dotnet format --verify-no-changes --no-restore
git diff --check
git diff
git status --short --branch
```

The build treats warnings as errors, enables nullable reference types and SDK analyzers,
and uses no warning suppression. `dotnet format --no-restore` applies formatting.
Review non-C# whitespace/layout manually; dotnet format is not a general YAML/XAML/Markdown formatter.
Check untracked files explicitly: they do not appear in ordinary `git diff`.

Windows CI runs the first four commands after checkout and SDK setup. Restore may download
test dependencies. Tests themselves use no network services and never access real instances.
Hosted CI results must be observed separately from local verification.

## Meaningful architecture tests

| Suite | Current checks |
| --- | --- |
| Domain.Tests | No outward project references/UI in Debug or Release; compiled dependency boundary; no direct filesystem implementation types |
| Application.Tests | Only Domain project/assembly dependencies; no UI properties/frameworks |
| Infrastructure.Tests | Only Application/Domain project/assembly dependencies; no UI properties/frameworks |

Project checks invoke `dotnet msbuild` with `-getItem` / `-getProperty`, without a build.
This evaluates imported and conditional references, catching even unused forbidden dependencies.
A 60-second timeout bounds each process. Tests require an SDK and repository checkout.
Compiled checks use BCL PE/metadata readers; they do not need reflection, dynamic product
assembly loading, an architecture package, or dummy production marker classes.

There are ten cases: two evaluated configurations and one compiled check per layer,
plus the Domain filesystem check. The compiled check uses the configuration built by the
test command; querying Debug properties does not claim a Debug build was run.
The filesystem check also rejects path-taking StreamReader/StreamWriter constructors,
while allowing their caller-owned Stream overloads.
Future product behavior tests belong alongside these checks, not in the shared helper.

Prove regression guards with a controlled negative check: temporarily add an unused forbidden
project reference, run the already-built relevant architecture test with `--no-build`, confirm
its assertion fails, restore the project file, and re-run. Never commit the invalid dependency.

## Limits and future coverage

Architecture guards are not exhaustive semantic analysis. Review indirect IO, native calls,
unusual UI packages, custom MSBuild logic, and rule placement. Pure Domain code may use
path/value representations but must not perform filesystem IO.

- **Domain:** table-driven migration rules, exclusions, preset defaults, plan invariants.
- **Application:** transitions, cancellation, partial failures, and result classification through controlled ports.
- **Infrastructure:** real IO in exclusively owned temporary roots; permission/lock failures,
  containment, symlinks/junctions, case collisions, stale plans, partial writes, and cleanup.
- **UI:** focused view model tests when behavior exists; Windows UI smoke checks when needed.

Rules, backup, rollback, verification, and destructive writes require integration,
failure-path, and regression coverage. Define failure injection before those implementations.
Verify failed backup prevents writes, execution failure preserves recovery evidence,
failed verification triggers diagnosis, and failed rollback remains recovery-required.

Never use real Minecraft worlds, personal directories, or network APIs as test fixtures.
Tests that create data must control and validate their output root, avoid surprise overwrites,
and clean up only their owned directory. Do not persist private payloads or identifiers.

## Phase 1 entry checks

Scope Inspector to reads. Define explicit input roots, safe observation models, missing versus
inaccessible results, reparse-point policy, bounded enumeration, cancellation, and redacted errors.
Add behavior tests before connecting UI. Phase 0 architecture tests alone do not prove these properties.

## Phase 1 coverage and negative evidence

- Domain: expected/actual kind, missing versus incomplete summaries, immutable result collection.
- Application: exact eleven-name catalog, root failure without child lookups, partial failure,
  cancellation before/during inspection, unexpected exceptions and session disposal.
- Infrastructure: owned temporary Windows fixtures; missing/file/empty roots, mismatches,
  unknown names, nested locked content, real ACL denial, root/child/ancestor junctions,
  unsafe inputs and names, and retained ancestor lifetime.
- App.Tests: input invalidation, folder-picker cancellation, duplicate-start prevention,
  cancellation of a pending operation (including a late result), and path-free error display.

The read-only integration test snapshots relative names, file bytes, attributes and last-write
timestamps before/after real inspection. Test setup/snapshot/cleanup may read/write fixture data;
the product Inspector may not. Last-access times are excluded because Windows may update them.
Only owned temporary directories are cleaned; ACLs are restored and junctions removed first.

Controlled experiment performed during implementation: temporarily inserted a file write into
the adapter. `ReadsOnlyKnownMetadataAndChangesNothing` failed its snapshot equality assertion.
Removed the write; the same test passed. The violating code was never committed.

Review also identified an in-place reparse conversion race. A fixture converts an open root into
a junction using an attribute-only handle; the former absolute-path child lookup incorrectly
observed the target directory (RED). Handle-relative lookup now receives Windows status
`STATUS_REPARSE_POINT_NOT_RESOLVED` and reports Unavailable (GREEN),
and the fixture remains as `RootConvertedToJunctionDuringSessionNeverExposesTargetChildren`.

These checks do not prove behavior on every Windows filesystem/filter or provide an atomic
snapshot. UI automation and hosted CI are reported separately; a passing ViewModel test is not
a visual smoke test. Future writes require their own containment, collision and recovery design.
