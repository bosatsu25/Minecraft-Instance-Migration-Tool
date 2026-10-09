# Minecraft Instance Migration Tool

[日本語](README.ja.md) · [Releases](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/releases) · [Installation](docs/install.md) · [Japanese guide](docs/member-guide.ja.txt)

A free Windows desktop app for **selectively and safely migrating Minecraft user data** between existing instances when changing mod packs or launch configurations.

**Latest public release: [v1.0.0](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/releases/tag/v1.0.0), October 9, 2026.** Supported distribution: **Windows 11 x64, unsigned community edition**. The installer and portable ZIP include .NET; users do not need to separately install .NET, Visual Studio, Python, or paid services.

## Download v1.0.0

| Package | How to use | Download |
| --- | --- | --- |
| Installer | Install once, then launch from the Start Menu | [Windows x64 setup.exe](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/releases/download/v1.0.0/MinecraftInstanceMigrationTool-1.0.0-win-x64-setup.exe) |
| Portable ZIP | Extract **the entire ZIP**, then run `MinecraftInstanceMigrationTool.exe` | [Windows x64 ZIP](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/releases/download/v1.0.0/MinecraftInstanceMigrationTool-1.0.0-win-x64.zip) |
| Checksums | Verify downloaded packages against a trusted checksum | [SHA256SUMS.txt](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/releases/download/v1.0.0/SHA256SUMS.txt) |

Both editions include the `START-HERE.ja.txt` member guide, license, third-party notices and runtime. The installer is per-user; the ZIP executable must stay alongside the other extracted files.

**Unsigned packages may show Unknown Publisher / SmartScreen warnings.** Confirm the trusted download source and compare with `SHA256SUMS.txt`. SHA-256 confirms equality with the provided checksum, **not publisher identity**. Do not disable Windows security protections; if uncertain, cancel and contact the distributor.

## Quick start

1. Close Minecraft and its launcher; separately back up important data.
2. Install or extract the full ZIP. Select Japanese/English and Windows/light/dark appearance in the toolbar (settings last for this session only).
3. Select the old **Source** and new **Destination** game folders (containing `options.txt` or `config`). Both folders must already exist and must not overlap.
4. Inspect and generate **Preview**. Include/exclude the data you want. **Recommended excludes `saves` and `screenshots`**; opt in if you need them.
5. For destination conflicts, explicitly choose **Skip** or **Replace**; apply choices. Replace is not a Merge.
6. Select a **safety workspace** outside both instances for backups and journals. Run the capacity check.
7. Confirm **Execute**, then inspect **Report** for `Completed` and successful verification.
8. Preserve the safety workspace and your original backups until you verify the new instance.

See [full installation and migration instructions](docs/install.md) and the [Japanese step-by-step guide](docs/member-guide.ja.txt).

## Features and limitations

- Eleven selectable entries: `options.txt`, `config`, `resourcepacks`, `shaderpacks`, `schematics`, `saves`, `screenshots`, `XaeroWaypoints`, `XaeroWorldMap`, `itemscroller`, `g4mespeed`.
- Read-only inspection/Preview, Include/Exclude, Recommended, Select all/none, explicit Skip/Replace, capacity preflight, verified Replace backups, durable execution/recovery evidence and independent post-write verification.
- Fingerprint-guarded rollback requires explicit confirmation and backend approval. **No automatic rollback or resume.**
- `hanemod-client.json` is excluded case-insensitively throughout selected directories; existing excluded files at the destination survive Replace.
- No launcher auto-detection, `mods` transfer, mod/loader/version compatibility checks, Merge, report export, exact NTFS clone or Windows 10 support claim. **A safe copy does not imply game compatibility.**

See [ModPackTransfer comparison](docs/modpacktransfer-compatibility.md) and [architecture](docs/architecture.md).

## v1.0.0 validation

The [published-tag GitHub Actions run](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/actions/runs/37916083931) for [`2faebe8`](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/commit/2faebe8be3c5fc382b91f046a11eb0eed1f81886) passed on October 9, 2026:

- **458/458 deterministic tests** passed (Domain 110, Application 136, Infrastructure 131, App 81).
- **10/10 WPF UI tests** passed, including packaged-build UI verification.
- **0 build warnings and 0 errors**; version/checksum, self-contained launch, and installer installation/reinstallation/uninstallation checks passed.
- Published assets are **unsigned-community**, not Authenticode-signed. Optional production signing is not required for this edition.

See [v1.0.0 release](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/releases/tag/v1.0.0), [release process](docs/release.md) and [validation checklist](docs/release-validation.md) (which contains older and outstanding manual evidence).

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
Backup Workspace Safety
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

**Complete for the public unsigned v1.0.0 community edition (October 9, 2026).** The .NET 10 self-contained Windows 11 x64 application ships as portable ZIP and per-user Inno Setup installer. The [tag workflow](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/actions/runs/37916083931) passed packaging, version/checksum, launch, installation/reinstallation/uninstallation and packaged UI checks.

The About dialog shows version metadata and the final multi-resolution icon is included. Single-file publishing, trimming and NativeAOT remain disabled. Optional Cloud HSM Authenticode signing is separate from the published **unsigned** assets. See [distribution validation](docs/release-validation.md) for manual checks that remain outside CI.

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

## Published v1.0.0 packages

**[GitHub Release v1.0.0](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/releases/tag/v1.0.0) is publicly available**, not only locally validated:

- [`MinecraftInstanceMigrationTool-1.0.0-win-x64-setup.exe`](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/releases/download/v1.0.0/MinecraftInstanceMigrationTool-1.0.0-win-x64-setup.exe) — per-user installer.
- [`MinecraftInstanceMigrationTool-1.0.0-win-x64.zip`](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/releases/download/v1.0.0/MinecraftInstanceMigrationTool-1.0.0-win-x64.zip) — fully extracted portable edition.
- [`SHA256SUMS.txt`](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/releases/download/v1.0.0/SHA256SUMS.txt) — matching release checksums.

See [install](docs/install.md) and [release process](docs/release.md) for checksums, unsigned warnings, upgrades, and support. This unofficial community tool is not affiliated with Mojang Studios or Microsoft.

## Not implemented yet

The public v1.0.0 edition does not claim:

- Report persistence or user export
- Automatic rollback or recovery resume
- Merge conflict semantics
- Minecraft/mod/loader compatibility analysis or launcher auto-detection
- Exact NTFS cloning or Windows 10 support
- Signed production binaries (the published release is intentionally unsigned)
- Additional manual or environment-specific checks listed in [distribution validation](docs/release-validation.md)

**Safe file copying is not equivalent to target-instance compatibility.**

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

22. **Phase 5.0 — Release hardening (complete for unsigned v1.0.0)**
23. **Phase 5.1 — Member distribution / public v1.0.0 release (complete; further manual validation documented)**

See the [ModPackTransfer compatibility matrix](docs/modpacktransfer-compatibility.md) for the inspected
reference inventory and regression evidence, [migration rules](docs/migration-rules.md) for candidate rules,
[rollback](docs/rollback.md) for rollback guarantees,
and [execution journal](docs/execution-journal.md) for execution evidence.
Report projection and its persistence boundary are documented in [report](docs/report.md).

See [AGENTS.md](AGENTS.md) for working agreements,
[testing](docs/testing.md) for verification scope,
and [graph loop](docs/graph-loop.md) for the evidence-driven development loop.
