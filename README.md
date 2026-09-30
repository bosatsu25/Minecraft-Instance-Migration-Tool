# Minecraft Instance Migration Tool

[日本語](README.ja.md)

A Windows desktop application for selectively and safely migrating Minecraft user data
from an old instance to a new one when changing mod packs or launch configurations.

**Status: Phase 5.0 release hardening and the final application icon are implemented. Formal release readiness awaits production signing configuration. Phase 5.1 covers clean-machine validation.**
Application now owns the product-level workflow that connects inspection, planning, preview,
backup preparation/execution, and migration execution through explicit session states.
The backend also includes verified Backup, durable execution and rollback-attempt journals,
Windows Copy / Replace, independent post-write verification, and fingerprint-guarded rollback.

The WPF UI exposes Inspector, Migration Preview, selection/conflict editing, explicit execution
confirmation, Backup when required, verified Copy / Replace execution, Recovery diagnosis,
explicitly confirmed guarded rollback, and a read-only Migration Report.
Execution also requires a current capacity preflight result for the selected safety workspace.
The inspected ModPackTransfer migration feature set is covered by a traceable compatibility matrix
and regression tests. Against reference commit `e174cdac8229f3e061175a36121d55db01961452`,
18 migration-relevant behaviors were inventoried and the compatibility matrix contains **0 Missing**
items. The successor keeps the same eleven migration candidates while strengthening overwrite, path,
backup, verification, rollback, error-redaction, and capacity behavior.

The Phase 4.6 verification baseline is **429 unit/integration tests + 6 FlaUI UI smoke tests**, with
0 build warnings/errors and hosted `verify` / `ui-smoke` checks green before merge.

Phase 5.0 fixes development version `0.9.0` and prepares a Windows 11 x64 self-contained folder publish,
portable ZIP, per-user Inno Setup installer, SHA-256 checksums, assembly-derived About display, and a
least-privilege release workflow. It does not create a `v1.0.0` tag or public release.

## Current implementation

The backend execution path is implemented through:

```text
Inspect
  ↓
Plan
  ↓
Preview / Dry Run
  ↓
Capacity Preflight
  ↓
Backup Preflight
  ↓
Backup IO
  ↓
Backup Revalidation
  ↓
Execution Workspace Safety
  ↓
Durable Execution Journal
  ↓
Live Revalidation
  ↓
Copy / Replace
  ↓
Independent Post-write Verification
  ↓
Durable Applied / Failed evidence
```

Rollback after failed verification is also implemented:

```text
Execution evidence
  ↓
RollbackPlan
  ↓
Backup Revalidation (Replace only)
  ↓
Durable Rollback Started
  ↓
Fingerprint-guarded Rollback IO
  ↓
Applied / GuardRejected / Failed
  ↓
Durable Rollback Attempt evidence
```

If a rollback attempt is reopened with durable `Started` but no terminal record,
it recovers as `Uncertain`. The implementation does not guess whether the action succeeded,
failed, should be replayed, or should be skipped.

Phase 3.8 makes recovery diagnosis durable; it does **not** implement automatic resume.

### Phase 4.0 Application workflow / session

Phase 4.0 adds an Application-owned workflow boundary over the existing use cases.
The workflow is now the authority for product-level session transitions; callers can read
`MigrationWorkflowSession` evidence but cannot publicly construct arbitrary session states
or mutate its state/evidence setters.

```text
SelectRoots
  ↓
Inspect
  ↓
ConfigurePlan
  ↓
Preview
  ↓
ReadyForBackup
  ↓
BackupReady
  ↓
ReadyForExecution
  ↓
Executing
  ↓
Completed / Cancelled / Blocked / RecoveryRequired
```

The audited Phase 4.0 behavior includes:

- root reselection starts from fresh workflow evidence instead of retaining prior plan/backup/execution state
- selection and conflict inputs are defensively copied
- reconfiguring the plan invalidates downstream Preview / Backup / Execution evidence
- a `NeedsDecision` or otherwise non-ready preview cannot advance to backup
- `BackupPlanStatus.NotRequired` advances without inventing a backup path or invoking backup IO
- a real replacement backup requires a backup parent
- execution still requires a journal parent before it can start
- exceptions or cancellation escaping from execution are treated conservatively as `RecoveryRequired`

Phase 4.0 itself did not add WPF behavior or new filesystem adapters. Phase 4.1 connected
selection and Skip / Replace conflict editing; Phase 4.2 connects confirmed execution.

## Phase 4.1 selection / conflict editing

The Migration Preview is now connected to the Phase 4.0 workflow as its orchestration boundary.
Recommended remains the initial selection, but users can select a preview row and explicitly include or exclude it.
For a current destination conflict, the UI exposes only the already-defined `Skip` / `Replace` decisions; there is still no Merge behavior.
Choice edits are pending until **Apply choices** rebuilds the plan and preview from the existing inspection evidence, so editing does not re-inspect or write files.
Changing source/destination clears the prior session choices, and **Reset Recommended** restores the default preset.

## Phase 4.2 confirmed execution

A Ready preview can be executed only after all pending choices are applied and a separate safety
workspace is selected. The safety workspace holds the durable execution journal and, for Replace,
the verified backup artifact. A dedicated confirmation dialog displays Copy / Replace / Skip counts,
whether backup is required, and the source and destination before any filesystem write begins.

The ViewModel advances the existing `IMigrationWorkflow` through Prepare Backup, Backup, Prepare
Execution, and Execute. Copy-only plans use the workflow's `NotRequired` backup result and never call
backup IO. The UI projects `Completed`, `Blocked`, `Cancelled`, and `RecoveryRequired` from workflow
evidence; `RecoveryRequired` locks the session against editing or another Execute.

## Phase 4.3 recovery diagnosis and guarded rollback

When execution returns `RecoveryRequired`, Application reloads the durable execution journal,
revalidates any required replacement backup, and creates the existing Domain `RollbackPlan`.
The UI shows typed Applied / Failed / Uncertain evidence and distinguishes rollback available,
blocked, and manual recovery required without interpreting journal records itself.

Rollback never starts automatically. It is enabled only for a backend-authorized Ready plan and
requires a dedicated confirmation showing Delete-created / Restore-backup counts. The existing
rollback executor persists durable Started evidence before guarded filesystem mutation. The UI
projects durable attempt evidence as Recovered, GuardRejected, Failed, or Uncertain. GuardRejected
means the destination was left unchanged because current content no longer matched the migration
fingerprint. Uncertain is never treated as retryable; automatic resume remains out of scope.

## Phase 4.4 read-only Migration Report

Application projects existing Preview, Backup, Execution, Verification, Recovery, and Rollback
evidence into a typed report. It distinguishes Completed, Cancelled, Blocked, RecoveryRequired,
Recovered, GuardRejected, Failed, and Uncertain without changing workflow state or granting
execution/rollback authority. Inconsistent or incomplete evidence fails closed as an unavailable
report instead of inventing a successful result.

The report tab shows action counts, backup outcome, verification outcome, and recovery details only
when recovery evidence exists. The report model contains no filesystem paths or raw exception text.
Phase 4.4 keeps reports in memory: automatic persistence and user export remain deferred until an
owned destination, collision policy, and partial-write strategy are defined. See [report](docs/report.md).

## Phase 4.5 capacity / free-space preflight

Before Backup or migration writes can begin, Application measures logical source bytes for Copy and
Replace, plus current destination bytes that Replace must back up. Infrastructure performs that bounded
measurement through the existing handle-relative, no-follow Windows traversal. It also resolves canonical
volume identities and available capacity, so destination and safety workspace requirements are combined
when both paths share a physical volume, including aliases such as SUBST.

The estimate adds a bounded reserve: 5% of logical bytes, with a 64 MiB minimum and 1 GiB maximum.
Unavailable measurements, unsafe trees, invalid values, overflow, and cancellation never produce Ready.
Changing roots, choices, Preview, or safety workspace invalidates the result. Execute stays disabled until
the current Preview and workspace have a Ready result. This preflight reduces obvious capacity failures;
execution still performs live validation and handles disk-full or other IO failures conservatively.
See [capacity preflight](docs/capacity-preflight.md).

## Phase 4.6 ModPackTransfer compatibility closure

Phase 4.6 audited the reference `TaichiServer/ModPackTransfer` implementation at commit
`e174cdac8229f3e061175a36121d55db01961452` and recorded 18 user-visible or
migration-relevant behaviors in a traceable compatibility matrix. There are no `Missing` rows.

The legacy `hanemod-client.json` exclusion is now an explicit, Domain-owned migration-content rule.
It applies case-insensitively at every directory depth and is used consistently by Copy, Replace
preservation, Backup, Rollback restore, independent verification/fingerprinting, capacity measurement,
Preview, and Report projection. Similar filenames are not excluded.

Legacy behaviors that would weaken safety are intentionally represented by safer equivalents rather than
copied literally: unconditional overwrite becomes explicit Skip/Replace, raw recursive copy becomes
handle-relative no-follow traversal, and Replace retains verified Backup, live revalidation, durable
journaling, independent verification, guarded rollback, and capacity gating.

See the [ModPackTransfer compatibility matrix](docs/modpacktransfer-compatibility.md) for the full
18-item inventory, reference locations, automated regression evidence, and intentional safer differences.

## Phase 5.0 release hardening

Version `0.9.0` is published for Windows 11 x64 as a .NET 10 self-contained folder. The release workflow
builds a portable ZIP and per-user Inno Setup installer, verifies version metadata and SHA-256 checksums,
and exercises install/uninstall on a hosted Windows runner. The application shows its assembly version in
About. Pull-request and branch dry-runs are unsigned; production signing requires a trusted tag context.

Single-file, trimming, and NativeAOT remain disabled for this release. The application uses the final
multi-resolution icon; production signing configuration is pending. Phase 5.1 will validate a clean-machine install, launch,
migration, recovery/rollback, artifact, signature, upgrade, and uninstall before a `v1.0.0` release.

## What the current UI can do

### Inspector

Select any local folder name and observe these eleven known direct children read-only:

- `options.txt`
- `config`
- `resourcepacks`
- `shaderpacks`
- `schematics`
- `saves`
- `screenshots`
- `XaeroWaypoints`
- `XaeroWorldMap`
- `itemscroller`
- `g4mespeed`

Expected kind and observed state are displayed separately.
Missing / Inaccessible / Unavailable / ReparsePoint remain distinct.
Junctions, symlinks, and other reparse points are never intentionally followed.

### Migration Preview / Dry Run

Source and Destination are inspected read-only and projected through the Recommended preset.

Recommended leaves `saves` and `screenshots` OFF by default.
Existing destination conflicts are shown as `NeedsDecision`.
The Preview UI can set Skip / Replace or clear a decision back to unresolved, then explicitly apply
those choices without reinspecting either root.

Generating and editing Preview remains metadata-only. Execution starts only after explicit confirmation,
then revalidates live state through the existing Application workflow before writing.

## Safety model

This is not a simple recursive-copy tool. The backend is structured as a migration engine
that attempts to preserve evidence about what happened when operations fail.

### Windows filesystem

- absolute local-drive paths only
- handle-relative traversal
- retained parent handles
- no-follow reparse policy
- nested junction / symlink / reparse-point failure is fail-closed
- lexical overlap plus canonical handle-path overlap checks
- physical aliases such as SUBST are considered
- Copy uses create-only semantics
- Replace requires live-state and backup revalidation
- user-provided paths are treated as untrusted input

### Backup

Only Replace entries require a pre-write backup.

Implemented backup properties include:

- owned backup roots
- ownership marker
- versioned completion manifest
- planned top-level membership checks
- recomputed tree fingerprint
- nested reparse rejection
- read-only validation of completed backup artifacts
- failed / cancelled artifacts are not accepted as recovery evidence

Backup is not an exact NTFS clone. ACLs, alternate data streams, and complete
timestamp / metadata fidelity are not guaranteed.

### Durable execution journal

Before a write is allowed, `Started` must be durably persisted.
Only an independently verified mutation can receive durable `Applied`.

```text
Live Revalidation
  ↓
Backup Revalidation (Replace)
  ↓
durable Started
  ↓
single-entry mutation
  ↓
independent verification
  ↓
durable Applied
```

JSONL records carry SHA-256 checksums and a previous-checksum chain.
An acknowledged record succeeds only after `Flush(flushToDisk: true)`.

An unterminated final record may be treated as a torn tail.
A newline-terminated malformed, checksum-invalid, or state-invalid record fails closed.

### Independent verification

The mutation return value is not trusted by itself.

```text
source fingerprint #1
destination fingerprint
source fingerprint #2
```

If source #1 differs from source #2, verification reports `SourceChanged`.
If a stable source differs from destination, verification reports `VerificationMismatch`.

Fingerprints are path-free and cover relative tree structure, regular-file default-stream bytes,
file/directory counts, total bytes, and SHA-256.

### Guarded rollback

Rollback uses the execution journal's post-write fingerprint as a mandatory guard.

- Copy: delete only while the current destination still matches the execution fingerprint
- Replace: restore only after backup revalidation and while the current destination still matches
- nested destination / backup trees are retained by handle across guard and mutation boundaries
- data changed after migration by the user or another process is not automatically deleted or overwritten
- failures after destructive rollback begins become `RecoveryRequired`

### Durable rollback-attempt journal

Phase 3.8 persists rollback evidence separately from the immutable execution journal.

```text
NotStarted
   ↓
durable Started
   ↓
guarded rollback IO
   ↓
Applied / GuardRejected / Failed
```

A `Started` action without a terminal record reloads as `Uncertain`.

The attempt journal is bound to the exact `RollbackPlan`, including order, name,
expected kind, execution operation, rollback action, and post-write fingerprint.

Unknown JSON properties, duplicate properties, missing required fields,
checksum / chain / ordering mismatches are rejected.
Entry names must be single Windows names, preventing absolute paths or path fragments
from being serialized into the journal.

Checksums detect accidental corruption; they are not authentication against an actor
who can rewrite the complete journal and recompute its checksum chain.

## Architecture

```text
App (WPF)
   ↓
Application
   ↓
Domain

Infrastructure
   ↑
Application ports
```

- **Domain** — observations, MigrationPlan, selection/conflict policy, backup/execution/rollback policy
- **Application** — use cases, orchestration, and ports for external effects
- **Infrastructure** — Windows filesystem, backup, journals, mutation, verification, rollback adapters
- **App** — WPF/MVVM Inspector, Preview, selection/conflict, Execute, Recovery Diagnosis, Guarded Rollback, and Migration Report UI plus composition root
- **Tests** — Domain / Application / Infrastructure / App plus FlaUI UI smoke

Dependencies point inward. Application and Domain do not reference Infrastructure or UI.

Technology:

- C#
- .NET 10
- WPF
- MVVM
- System.IO / Windows native filesystem APIs
- System.Text.Json
- xUnit v3
- FlaUI
- GitHub Actions

Production code intentionally avoids unnecessary DI, MVVM, and logging frameworks.
The design primarily uses the BCL and explicit ports/adapters.

See [architecture](docs/architecture.md) for details.

## Installation status

The pipeline prepares these fixed artifact names:

- `MinecraftInstanceMigrationTool-0.9.0-win-x64.zip`
- `MinecraftInstanceMigrationTool-0.9.0-win-x64-setup.exe`
- `SHA256SUMS.txt`

There is no stable download yet. Phase 5.1 must validate the artifacts before `v1.0.0`. The installer is
per-user; the ZIP is portable; both are self-contained. See [install](docs/install.md) and
[release process](docs/release.md) for checksum, signing, upgrade, and support details.

This is an unofficial community tool and is not affiliated with Mojang Studios or Microsoft.

## Not implemented yet

The following are not complete:

- report persistence / user-initiated export
- automatic rollback resume
- Merge conflict semantics
- Minecraft / mod / loader compatibility analysis
- exact NTFS clone semantics
- production Authenticode signing and stable release publication
- clean-machine install / upgrade / migration validation (Phase 5.1)

In particular, **being safe to copy is not the same as being compatible with the target instance**.
The current implementation does not claim compatibility.

## Development

Use Windows with the .NET 10 SDK selected by [global.json](global.json)
(10.0.401, latest patch in that feature band).

```powershell
dotnet restore
dotnet build --configuration Release --no-restore
dotnet run --project src/MinecraftInstanceMigration.App --configuration Release --no-build
```

Libraries target `net10.0`; the WPF host targets `net10.0-windows`.
Restore requires NuGet connectivity, but the application itself has no network functionality.

## Verification

Deterministic gate:

```powershell
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build --no-restore
dotnet format --verify-no-changes --no-restore
git diff --check
git status --short --branch
```

WPF UI smoke:

```powershell
dotnet test tests/MinecraftInstanceMigration.UiTests/MinecraftInstanceMigration.UiTests.csproj --configuration Release --no-restore
```

GitHub Actions runs build / test / format plus UI smoke on Windows.
Warnings fail the build.

High-risk filesystem and rollback tests use only owned temporary fixtures.
Tests never intentionally mutate a real Minecraft instance.

See [testing](docs/testing.md) for details.

## Roadmap

Implemented:

1. Phase 0 — solution / layer boundaries / tests / CI / minimal WPF shell
2. Phase 1 — read-only Instance Inspector
3. Phase 2 — deterministic Migration Planner
4. Phase 2.1 — Recommended preset + Skip / Replace conflict intent
5. Phase 2.2 — Preview / Dry Run + WPF preview
6. Phase 3.0 — Backup Preflight
7. Phase 3.1 — Windows Backup IO
8. Phase 3.2 — completed-backup revalidation
9. Phase 3.3 — execution journal / rollback contract
10. Phase 3.4 — durable execution journal
11. Phase 3.5 — live revalidation + execute orchestration
12. Phase 3.6 — Windows Copy / Replace + independent verification
13. Phase 3.7 — guarded rollback IO
14. Phase 3.8 — durable rollback-attempt journal
15. Phase 4.0 — Application-owned migration workflow/session
16. Phase 4.1 — selection / conflict editing UI
17. Phase 4.2 — confirmed end-to-end Execute UI
18. Phase 4.3 — Recovery Diagnosis / Guarded Rollback UI
19. Phase 4.4 — read-only Migration Report
20. Phase 4.5 — capacity / free-space preflight
21. Phase 4.6 — ModPackTransfer compatibility closure

Next major areas:

22. **Phase 5.0 — Release hardening (implementation complete; production signing pending)**
23. Phase 5.1 — v1.0 clean-machine release validation

See the [ModPackTransfer compatibility matrix](docs/modpacktransfer-compatibility.md) for the inspected
reference inventory and regression evidence, [migration rules](docs/migration-rules.md) for candidate rules,
[rollback](docs/rollback.md) for rollback guarantees,
and [execution journal](docs/execution-journal.md) for execution evidence.
Report projection and its persistence boundary are documented in [report](docs/report.md).

See [AGENTS.md](AGENTS.md) for working agreements,
[testing](docs/testing.md) for verification scope,
and [graph loop](docs/graph-loop.md) for the evidence-driven development loop.
