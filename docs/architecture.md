# Architecture

## Layer boundaries

| Project | Responsibility | Allowed product references |
| --- | --- | --- |
| Domain | Pure observation models; future migration policies | None |
| Application | Use cases, orchestration, ports for external effects | Domain |
| Infrastructure | Filesystem and other adapters implementing Application ports | Application, Domain |
| App | WPF views, MVVM view models, composition | Application, Infrastructure |

Dependencies point inward. App may construct Infrastructure at the composition root;
view models depend on Application contracts, not concrete adapters. Infrastructure must
never be referenced by Application/Domain. Test projects are not production dependencies.
The UI smoke project is an external consumer of the built WPF app. FlaUI stays in that
test project and does not enter any product layer.

Only App targets `net10.0-windows` and enables WPF. Other projects target `net10.0`.
The Windows metadata implementation remains in Infrastructure; its runtime rejects non-Windows hosts.

## MVVM

Views own layout and UI-only behavior. View models translate UI commands into
Application requests and expose presentation state. Rules, exclusions, plan validation,
backup policy, and verification decisions must remain outside views/view models.
InspectorViewModel exposes input, busy/cancel state and observations. MainWindow composes the
use case and native adapter and supplies the folder picker. No MVVM framework or DI container is needed.

## Phase 1 observation contract

`IInstanceInspector.InspectAsync` accepts a candidate path and cancellation token.
`IInspectionFileSystem.OpenRoot` returns a disposable `IInspectionSession`: root state plus
single-name `ObserveChild`. Application requests exactly eleven catalog names, independent of UI.
The result has no paths or content. Expected kind and actual state are distinct; mismatch is
observable data, not migration eligibility. Domain defensively copies the observations.

States: Missing, File, Directory, ReparsePoint, Inaccessible, InvalidPath, Unavailable.
A non-directory root produces no child observations. ReparsePoint at the root also means
an ancestor blocked lookup. Unavailable includes sharing violations and other IO errors.
`KnownEntries` distinguishes NotInspected, NoneObserved, Present, and Indeterminate.
Presence takes precedence over unknown siblings; `IsComplete` separately exposes gaps.
Complete means all requested metadata observations succeeded (including known absence/links),
not that contents, Minecraft validity, compatibility, or migration safety were checked.

The Windows adapter opens existing objects for attributes only. It opens the drive root once,
then uses single-component `NtCreateFile` names relative to retained parent handles, with
`OBJ_DONT_REPARSE`, `FILE_OPEN_REPARSE_POINT`, and `FILE_OPEN_NO_RECALL`.
It queries attributes on the resulting handles, never reopens children by absolute path.
Ancestors deny write/delete sharing to prevent rename; sharing alone does not prevent reparse
conversion, which is why handle-relative resolution is required. No data/list/write access,
enumeration, content read, creation, or diagnostic file is requested by the adapter.
Input validation rejects nonlocal/ambiguous path syntax before native observation.

Native contract references: [NtCreateFile](https://learn.microsoft.com/en-us/windows/win32/api/winternl/nf-winternl-ntcreatefile),
[OBJECT_ATTRIBUTES](https://learn.microsoft.com/en-us/windows/win32/api/ntdef/ns-ntdef-_object_attributes),
[CreateFile](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew).
Windows may permit attribute reads via the parent's listing permission; the ACL fixture denies
both routes ([Microsoft explanation](https://devblogs.microsoft.com/oldnewthing/20150428-00/?p=44994)).

Cancellation remains OperationCanceledException in the use case; unexpected programming errors
propagate. The UI catches boundary failures, displays their exception category without private
messages, and permits retry. Input is held only in memory. No logs or reports are persisted.
Metadata calls run off the UI thread and cannot be interrupted mid-call. Results are non-atomic,
can become stale, and must never authorize future writes without revalidation.


## Phase 2 plan contract

Planning is deterministic and performs no filesystem IO. `KnownEntryCatalog` is now the single
ordered definition of the eleven top-level observation targets, so Inspector and Planner cannot
silently drift to different candidate sets.

`IMigrationPlanner.CreatePlan` accepts source and destination `InstanceInspectionResult` values
plus an explicit set of selected known names. Application exposes the use case; Domain owns the
policy and immutable plan models.

The plan preserves source/destination root states and per-entry observations. Each known candidate
receives one explicit disposition:

- unselected entries are `ExcludedBySelection`;
- missing selected sources are explicit no-ops (`SourceMissing`);
- a matching source with a missing destination is `ReadyToCopy`;
- an existing destination is `DestinationConflict` and requires a later collision decision;
- source/destination reparse points, inaccessible/unavailable observations, malformed/duplicate
  observations, source kind mismatches, and non-directory roots block the affected plan;
- unknown selection names are preserved and block the plan instead of being ignored.

Plan status is `Ready`, `NeedsDecision`, or `Blocked`. A blocker takes precedence over conflicts.
An incomplete observation for an unselected candidate does not block an otherwise safe selected
candidate. No collision policy, preset, nested exclusion, size/hash calculation, containment proof,
or execution authorization is inferred.

Phase 2 does not make a plan executable. Future execution must re-inspect/revalidate roots and
observations because Phase 1 snapshots are non-atomic and can become stale.


## Phase 2.1 selection and conflict policy

`KnownEntryDefinition.RecommendedByDefault` makes the legacy Recommended direction part of the
same catalog used by Inspector and Planner. `MigrationSelectionPresets.Recommended` selects all
known candidates except `saves` and `screenshots`; those remain explicit opt-in data.

Destination conflicts still have no implicit behavior. A caller may supply a typed
`DestinationConflictDecision` for a selected entry:

- `Unresolved` keeps `DestinationConflict` / `NeedsDecision`;
- `Skip` produces `SkippedDestinationConflict`, an explicit no-op;
- `Replace` produces `ReadyToReplace` and marks the entry as requiring backup before any future write.

Conflict decisions are accepted only for selected known entries that actually have a destination
conflict. Unknown, unselected, stale/non-conflicting, or unsupported decisions are preserved as
typed `ConflictDecisionIssue` values and block the aggregate plan instead of being silently ignored.

This phase intentionally does not define Merge behavior. Directory merge needs nested inventory,
collision ordering, exclusion precedence, containment, backup, and rollback semantics that the
current top-level metadata plan cannot prove.

The legacy `hanemod-client.json` exclusion is also still unresolved because the supplied product
brief does not establish whether it means one exact relative path, basename-at-any-depth, or another
case-insensitive Windows matching rule. No execution path exists yet, so no code may claim the
exclusion is enforced.


## Phase 2.2 Preview / Dry Run contract

Preview is a projection of an already-created `MigrationPlan`; it does not recompute selection,
conflict policy, or filesystem observations. `MigrationPreviewPolicy` maps each current plan
disposition to a user-facing typed action while preserving the original disposition for detail:

- `ReadyToCopy` -> `Copy`
- `ReadyToReplace` -> `Replace` and backup required
- `SkippedDestinationConflict` -> `Skip`
- `SourceMissing` -> `NoSource`
- `DestinationConflict` -> `NeedsDecision`
- unselected candidates -> `Excluded`
- safety blockers -> `Blocked`

Unknown future plan dispositions fail closed with an exception so Preview cannot silently invent a
meaning after Planner evolves. Preview copies plan status, unknown selections, and conflict-decision
issues into its own immutable snapshot.

The WPF Preview tab inspects source and destination with the existing read-only Inspector, creates a
Recommended plan, and projects that exact plan into Preview. It does not offer conflict editing yet:
an existing destination is visibly `NeedsDecision`. Changing either input clears the prior preview;
cancellation and error handling do not retain a late or partial result.

Phase 2.2 intentionally has no recursive size estimate, hash, nested file inventory, compatibility
analysis, or exclusion enforcement because Phase 1 metadata observation does not provide those facts.
No preview result authorizes a write. Future Backup / Execute must revalidate roots and observations
against the reviewed intent because both inspection and preview can become stale.


## Phase 3.0 Backup Preflight contract

Backup begins with a read-only Domain preflight rather than immediate filesystem copying.

`BackupPlanPolicy.Create` accepts an existing `MigrationPlan`:

- a migration plan that is not `Ready` produces a blocked backup plan;
- only entries with `ReadyToReplace` require destination backup;
- copy-to-missing-destination entries do not require backup;
- Skip / NoSource / Excluded entries do not require backup;
- replacement destinations must still be observed as File or Directory; malformed replacement
  intent fails closed instead of being silently accepted.

A backup plan is one of `NotRequired`, `Ready`, or `Blocked`. A `Ready` plan can produce a
versioned `BackupManifestDraft` containing only instance-relative known entry names, expected kinds,
and observed destination states. It deliberately contains no machine-specific source, destination,
or backup paths and is not evidence that any bytes were copied.

This phase adds no Infrastructure adapter and performs no production filesystem writes. That is
intentional. Before Backup IO is implemented, the next phase must define and test all of:

- where backup roots may be created and how destination/backup overlap is rejected;
- handle-safe traversal and containment for nested content;
- reparse point / junction policy at every depth;
- how partial backup state is represented after cancellation or failure;
- how backup completeness is verified independently of a copy success flag;
- how manifest entries bind to actual copied bytes without leaking private paths;
- what cleanup is safe after a failed backup, especially under path replacement races.

A preflight or manifest draft never authorizes Execute.


## Phase 3.1 Backup IO foundation

`BackupExecutor` is the Application boundary for starting backup IO. It rejects `Blocked`
backup plans before the filesystem port, returns `NotRequired` without creating storage, creates
the schema-v1 manifest draft itself, and preserves typed Completed / Cancelled / Failed outcomes.

`WindowsBackupStorage` is Windows-only and intentionally narrow:

- destination and backup-parent inputs must be absolute local-drive directories;
- the backup parent may not equal or be inside the destination root;
- path components are opened one-at-a-time relative to retained handles with
  `OBJ_DONT_REPARSE` / `FILE_OPEN_REPARSE_POINT`; nested traversal enumerates names from
  directory handles via `NtQueryDirectoryFile` and opens every child relative to its parent handle;
- any reparse point encountered at any traversed depth fails the backup;
- a unique `mim-backup-{guid}` directory is created relative to the held backup-parent handle;
- an ownership marker is written first; partial roots are retained on cancellation/failure and
  never receive the completion manifest;
- planned replacement entries are copied recursively without following links;
- after copy, the destination tree is fingerprinted, the backup tree is fingerprinted, and the
  destination tree is fingerprinted again. Source-before/source-after mismatch reports
  `SourceChanged`; source/backup mismatch reports `VerificationFailed`;
- `backup-manifest.json` is written only after verification succeeds and contains no absolute paths.

The fingerprint covers deterministic relative structure plus default-stream file bytes, file count,
directory count, and total bytes. It is an integrity signal for the copied Minecraft payload, not a
general NTFS clone. Phase 3.1 does **not** preserve ACLs, alternate data streams, or full filesystem
metadata such as timestamps. Execute / restore must not assume those properties were preserved.

Backup result paths remain in-memory return values. Failure categories do not include exception
messages that might contain private user paths.

## Migration Engine direction

Develop one node at a time. Inspect and the read-only Planner core are implemented; later nodes remain separate changes.

```text
Inspect → Plan → Preview / Dry Run → Backup → Execute → Verify → Report
                                                   Verify failure
                                                          ↓
                                                      Diagnose
                                                          ↓
                                                      Rollback
                                                          ↓
                                                       Report
```

- **Inspect:** Infrastructure reads explicitly selected instance roots; Application returns
  observations. No destination creation or modification. Unknown/inaccessible entries are
  reported as incomplete inspection, not silently treated as absent.
- **Plan:** Domain produces explicit top-level candidate intent, selection defaults, blockers, and typed
  destination conflict intent. Nested exclusions, Merge behavior, compatibility, and execution state remain separate.
- **Preview / Dry Run:** The implemented read-only preview presents the same plan without writes,
  including exclusions, planned actions, blockers, unresolved conflicts, and backup intent. Size estimates
  remain unavailable until a later bounded inventory design exists. Do not compute a different implicit plan at execution.
- **Backup:** Phase 3.1 implements an owned Windows backup artifact for planned replacements,
  handle-relative no-follow traversal, partial-state preservation, and independent post-copy fingerprint
  verification. Completion manifest creation is the final step; a failed/cancelled root is never complete.
- **Execute:** Revalidate roots and source/destination state against the reviewed plan.
  Reject stale plans, unsafe paths, or changed collision assumptions; cancellation and partial writes need explicit outcomes.
- **Verify:** Compare actual outcomes against the plan using defined evidence (such as content
  hashes where required), independently of an executor's success flag.
- **Diagnose / Rollback:** Preserve failure evidence without private payloads. Attempt recovery
  using verified backup information and an execution journal. Rollback may fail and must
  never be reported as successful merely because it was attempted. Do not blindly undo later user edits.
- **Report:** Distinguish succeeded, failed, cancelled, partially changed, rolled back, and
  recovery-required outcomes. Define bounded, redacted diagnostics before persisting any report.

Application will own workflow transitions, cancellation, failure handling, and orchestration.
Domain will own deterministic rules and plan invariants. Infrastructure will own actual IO.
Define ports only when the first use case requires them; this document is not an API contract.

## Filesystem safety requirements for later phases

Reject identical/nested roots and traversal outside controlled roots. Account for Windows
case rules, junctions/reparse points, symlinks, locks, long paths, permission failures, and
time-of-check/time-of-use changes. Normalizing a string alone is not containment proof.
No silent overwrites; conflict policy belongs in the reviewed plan. Future archive extraction
must prevent Zip Slip. Source data is read-only unless a separate explicit request authorizes otherwise.

Tests must pin down behavior before high-risk writes are implemented. Integration tests use
owned temporary directories, never real instances. No network adapters are in scope.

## Enforcement and limits

Architecture tests check evaluated project references and UI properties in Debug/Release,
plus actual assembly metadata and a focused list of direct Domain filesystem types.
They inspect metadata without loading product assemblies dynamically.

These are regression guards, not a sandbox or full static analyzer: indirect third-party IO,
reflection, native calls, source-level business-rule placement, custom MSBuild target side
effects, and every possible UI framework are not exhaustively detected. Review dependency
changes and adapter behavior explicitly. See [testing](testing.md) and [ADR 0001](adr/0001-technology-and-architecture.md).
