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


## Phase 3.2 Completed backup revalidation

A completed backup is not trusted merely because Phase 3.1 once returned `Completed`.
`IBackupArtifactValidator` is the Application use case; the Infrastructure storage adapter
reopens the backup artifact read-only and proves it still matches the reviewed `BackupPlan`.

Validation requires all of the following:

- the backup plan is still `Ready`;
- the backup-root path is a supported local path and every ancestor/root is opened no-follow;
- the ownership marker exists, is valid schema v1, contains a GUID execution id, and is bound to
  the `mim-backup-{executionId}` directory name;
- `backup-manifest.json` exists, parses as the current schema, and its entry list exactly matches
  the current backup plan in order/name/kind/state;
- the backup root contains exactly the plan entries plus the two metadata files;
- no reparse point is encountered at any traversed depth;
- a newly computed tree fingerprint equals the manifest's file count, directory count, byte count,
  and SHA-256 value.

The validator performs no writes. Missing/corrupt metadata, extra top-level content, payload mutation,
plan mismatch, or reparse insertion invalidates the artifact. Validation returns typed categories
without surfacing path-bearing exception text.

A valid result is evidence that the backup artifact is internally consistent at validation time.
It is not a rollback implementation and does not authorize destructive writes by itself; a future
execution workflow must bind this evidence to an execution journal and revalidate destination/source
assumptions immediately before mutation.


## Phase 3.3 Execution journal and rollback contract

Phase 3.3 defines the deterministic recovery contract before migration writes are implemented.

`ExecutionJournalPolicy` converts only a `Ready` `MigrationPlan` into a schema-v1
`ExecutionJournalDraft`. Only write-intent entries are included, in reviewed plan order:

- `ReadyToCopy` -> `Copy`, and destination must still be observed as Missing;
- `ReadyToReplace` -> `Replace`, and the source/destination states must still match the intent;
- a non-ready plan or malformed write intent fails closed.

A Ready plan with no write entries produces `NotRequired`; this avoids inventing execution work
for Skip / NoSource / Excluded entries.

The runtime contract is represented by an immutable `ExecutionJournalSnapshot`. Each planned step
must preserve sequence, entry name, and operation and may report:

- `NotStarted`
- `Applied`
- `Failed`
- `Uncertain`

An `Applied` step needs a post-write `ExecutionContentFingerprint` before automatic rollback may
be considered. This fingerprint is intentionally path-free and carries file/directory counts,
total bytes, and SHA-256 evidence.

`RollbackPlanPolicy` consumes the draft plus a snapshot and produces rollback requirements in
**reverse execution order**:

- applied Copy -> `DeleteCreatedEntry`, guarded by the recorded post-write fingerprint;
- applied Replace -> `RestoreFromBackup`, guarded by the recorded post-write fingerprint and
  allowed only when completed-backup validation is currently valid;
- Failed / Uncertain -> `ManualRecoveryRequired`;
- Applied without a post-write fingerprint -> `ManualRecoveryRequired`;
- malformed schema or journal structure -> rollback `Blocked`.

The fingerprint guard is critical: future rollback IO must first prove that the current destination
still matches the state written by Execute. It must never blindly delete or overwrite a destination
that may have been edited after migration.

Phase 3.3 defines contracts only. It does **not** persist a journal, mutate destination data,
restore a backup, delete created entries, or claim crash durability. A future Infrastructure journal
must use durable append/flush semantics and an execution workflow must record evidence before advancing
to the next destructive operation.


## Phase 3.4 Durable execution journal storage

Phase 3.4 persists the Phase 3.3 logical journal without implementing migration writes.

`IExecutionJournalPersistence` is the Application boundary. It rejects blocked/not-required drafts,
invalid step numbers, and pre-cancelled calls before crossing the Infrastructure port.

`WindowsExecutionJournalStorage` writes one create-only `mim-journal-{guid}.jsonl` file under an
explicit existing local journal parent. The parent path is opened component-by-component with retained
handles and no-follow semantics. A reparse point in the journal path fails closed.

The journal is append-only at the logical level:

1. creation writes a header containing schema version, journal id, and the exact ordered draft entries;
2. `Started(sequence)` is appended and flushed to disk **before** a future executor may mutate that step;
3. `Applied(sequence, fingerprint)` or `Failed(sequence)` is appended and flushed after the attempt;
4. a later step cannot start until every earlier step is durably Applied.

Each line contains a SHA-256 checksum over its canonical payload and a previous-checksum pointer. This
is an integrity chain for corruption detection, not a secret-key tamper-proof signature.

Every acknowledged append writes a newline and calls `FileStream.Flush(flushToDisk: true)`. Cancellation
is honored before an append begins; once a journal append starts, the storage finishes or reports failure
rather than deliberately abandoning a half-written acknowledged transition.

Crash recovery deliberately distinguishes an unterminated trailing record from a completed malformed
record:

- bytes after the final newline are treated as an unacknowledged torn tail;
- Load ignores that torn tail read-only;
- the next successful transition truncates the torn tail before appending;
- a complete malformed/checksum-invalid line fails closed.

Therefore a durable `Started` with a torn/missing terminal record loads as `Uncertain`, while a torn
`Started` that was never acknowledged is ignored and the step remains `NotStarted`.

The journal file contains no absolute migration, backup, or journal-parent paths. Its file reference is
returned in memory to the caller. The header is bound to the current journal draft; a different draft
cannot reuse the file.

This phase does not claim whole-machine power-loss immunity beyond the guarantees exposed by Windows
file flush semantics, and it is not Execute. The future executor must await a successful durable Started
append before mutation and await a durable terminal append before advancing to another destructive step.


## Phase 3.5 Live revalidation and execute orchestration contract

Phase 3.5 wires the safety contracts together without adding a production mutation adapter.

Before each planned write step, `ExecutionLiveValidator` re-runs the read-only Inspector for the
source and destination roots and compares the current top-level observation for that step with the
reviewed `MigrationPlan`. Root state, expected kind, source state, destination state, and journal
operation must still match. Any drift blocks the step before durable `Started`.

For Replace steps, the completed backup artifact is also revalidated immediately before `Started`.
A missing or invalid backup prevents the destructive step.

The Application orchestrator then enforces this sequence:

```text
step live revalidation
  -> backup revalidation when Replace
  -> durable Started
  -> mutation port
  -> post-write fingerprint verification
  -> durable Applied
```

A later step is considered only after the previous step reached durable `Applied`.

If mutation or post-write verification fails after `Started`, the orchestrator attempts a durable
`Failed` terminal record using a non-cancelled safety path and returns `RecoveryRequired`.
If the `Applied` journal write itself fails after mutation/verification succeeded, the journal still
contains durable `Started`; the result is `RecoveryRequired`, so restart semantics remain
`Uncertain` rather than falsely claiming success.

Cancellation is honored before each destructive step. Once `Started` is durable, safety bookkeeping
uses non-cancelled journal writes so a cancellation cannot intentionally suppress terminal evidence.
If cancellation is observed between two safely Applied steps, execution stops before the next Started
record and returns the count of already Applied steps.

This phase introduces two ports but no Infrastructure implementation:

- `IExecutionMutationPort`: future single-entry Copy / Replace mutation;
- `IExecutionPostWriteVerifier`: future independent destination fingerprint calculation.

Therefore Phase 3.5 proves orchestration ordering and failure semantics only. It performs no production
Minecraft destination writes.


## Phase 3.6 Windows mutation and independent post-write verification

Phase 3.6 implements the two production ports introduced in Phase 3.5.

`WindowsExecutionMutationPort` accepts exactly one reviewed journal step. Both source and destination
roots must be distinct, non-overlapping local-drive directories. Their path components are opened
one-at-a-time with retained handles and no-follow semantics.

For Copy:

- the source top-level entry must still match the reviewed kind;
- the destination top-level name must still be absent;
- output nodes use create-only semantics;
- nested source entries are opened relative to held parent handles;
- any reparse point at any depth fails closed.

For Replace:

- source and destination top-level kinds are rechecked after durable Started;
- the existing destination tree is removed recursively by handle without following reparse points;
- source content is then copied create-only into the now-vacant reviewed name;
- races that make the destination missing/change/collide fail the mutation instead of overwriting an
  unexpected object.

Source and destination roots may not be equal or ancestor/descendant of each other. This is
checked both lexically and again after opening the roots by canonical handle path, so aliases such as
SUBST/short-name mappings cannot bypass the overlap guard. The execution workspace validator also
requires the durable journal parent to be physically outside both migration roots before the journal
is created. This prevents a Replace payload from deleting its own recovery evidence and prevents the
journal from contaminating source content.

`WindowsExecutionPostWriteVerifier` is independent of the mutation success flag. It fingerprints:

1. source entry,
2. destination entry,
3. source entry again.

The fingerprint covers deterministic relative structure, regular-file default-stream bytes,
file/directory counts, total bytes, and SHA-256. If source #1 != source #2, verification reports
`SourceChanged`; if stable source != destination, it reports `VerificationMismatch`.

Mutation still does not preserve ACLs, alternate data streams, or full timestamp metadata. Replace is
therefore a payload migration operation, not an NTFS clone.

A mutation failure after durable Started remains recovery-required by the Phase 3.5 orchestrator.
Phase 3.6 does not implement rollback.


## Phase 3.7 Guarded rollback IO

Phase 3.7 connects the existing `RollbackPlan` contract to Windows IO.

`RollbackPlanEntry` now carries `ExpectedEntryKind` from the original execution journal draft so
rollback can re-check the exact top-level type before touching destination data.

`RollbackExecutor` attempts automatic rollback only for `RollbackPlanStatus.Ready`. It preserves the
plan's reverse-execution order. `RestoreFromBackup` revalidates the completed backup artifact immediately
before each restore action; an invalid or missing backup blocks the action before storage mutation.

`WindowsRollbackStorage` enforces a mandatory current-state fingerprint guard:

- `DeleteCreatedEntry` opens the destination entry no-follow with delete access, fingerprints that held
  node, compares it with the execution journal's post-write fingerprint, and deletes that same held node
  only when it still matches.
- `RestoreFromBackup` verifies the held destination node against the post-write fingerprint, fingerprints
  the backup entry, removes the guarded destination node, copies the backup entry create-only, then
  fingerprints destination and backup again.
- backup/destination overlap is rejected lexically and again through canonical handle paths;
- any reparse point, kind drift, missing entry, or fingerprint mismatch before mutation is a guard
  rejection rather than an overwrite/delete;
- an error after rollback mutation begins is `RecoveryRequired`.

This is guarded rollback, not transactional rollback. There is no durable rollback-attempt journal yet.
A crash or IO failure during recursive delete/restore may leave partial rollback state. A later automatic
retry will normally fail the original post-write fingerprint guard and require manual recovery rather than
blindly repeating destructive work.


## Phase 3.8 Durable rollback-attempt journal

Phase 3.8 adds durable evidence around Phase 3.7 rollback actions without claiming automatic crash resume.

`RollbackExecutor` now requires an explicit rollback journal parent. Before the first automatic action,
`RollbackAttemptPersistence` creates a schema-v1 rollback-attempt artifact bound to the exact ordered
`RollbackPlan`: order, name, expected kind, execution operation, rollback action, and post-write fingerprint.

The Windows adapter persists `mim-rollback-{guid}.jsonl` with the same durability principles as the
execution journal:

- the journal parent must be physically outside destination and backup roots;
- the header is create-only and contains no absolute source/destination/backup paths;
- each line has a SHA-256 checksum plus previous-checksum pointer;
- each acknowledged line ends in a newline and is flushed with `Flush(flushToDisk: true)`;
- an unterminated final record is treated as an unacknowledged torn tail;
- a complete malformed/checksum-invalid record fails closed.

For every rollback action the required ordering is:

```text
backup revalidation when Restore
  -> durable rollback Started
  -> guarded rollback storage mutation
  -> durable Applied / GuardRejected / Failed
```

A later rollback action cannot start unless every earlier action is durably `Applied`.

On restart, a durable `Started` with no terminal record loads as `Uncertain`. That evidence means the
process cannot prove whether the destructive repair completed. Phase 3.8 does not automatically replay
or skip such an action. Recovery UI / policy must treat it as manual recovery unless a future phase adds
an independently provable resume protocol.

A `GuardRejected` terminal means the action did not pass the storage guard and no automatic later action
may start in that attempt. A `Failed` terminal means destructive rollback may be partial and therefore
requires recovery.

## Phase 4.0 Migration workflow / session

Phase 4.0 adds the Application-owned product workflow that connects the existing read-only planning,
backup, execution, and recovery ports. It does not add UI behavior or new filesystem adapters.

`MigrationWorkflowSession` is an immutable snapshot carrying only the evidence needed by the next
stage: selected roots, inspection results, selected names and conflict decisions, migration plan and
preview, backup plan/result, execution result, and a typed failure kind. It contains no private exception
messages and performs no filesystem access itself.

The allowed product sequence is:

```text
SelectRoots -> Inspect -> ConfigurePlan -> Preview
                                      |
                         plan not ready / conflict
                                      v
                                   Preview
                                      |
                                      v
                              ReadyForBackup
                                      |
                              ExecuteBackup
                                      v
                                BackupReady
                                      |
                              ReadyForExecution
                                      |
                                  Executing
                           /          |          \
                      Completed   Blocked   RecoveryRequired
```

`MigrationWorkflow` delegates each stage through existing Application contracts. It creates no backup,
journal, or migration artifact directly. A preview with unresolved decisions or blockers remains in
`Preview`, so a later selection/conflict UI can reconfigure the same session without bypassing planning.
Backup and journal parents are recorded before backup execution; execution can start only after a
completed or not-required backup result and a journal parent are present. Execution cancellation after
applied steps is surfaced as `RecoveryRequired` rather than a clean cancellation.

The workflow is an orchestration boundary, not a replacement for Domain policies. Selection, conflict,
backup, live validation, mutation, verification, and rollback safety remain owned by their existing
contracts.

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
- **Backup:** Phase 3.1 creates an owned verified artifact; Phase 3.2 can revalidate a completed artifact
  read-only against ownership, manifest, plan, exact top-level membership, no-follow traversal, and a
  recomputed tree fingerprint. A failed/cancelled/invalid artifact is never accepted as recovery evidence.
- **Execute:** Phase 3.5 provides orchestration; Phase 3.6 implements Windows single-entry mutation and
  independent verification. Future UI/execution entrypoints must still compose these ports only after
  Preview, backup, and journal setup succeed.
- **Verify:** Compare actual outcomes against the plan using defined evidence (such as content
  fingerprints), independently of an executor's success flag.
- **Diagnose / Rollback:** Phase 3.3 defines reverse-order rollback requirements; Phase 3.7 implements
  guarded Windows rollback IO. Automatic rollback requires the destination to still match its post-write
  fingerprint, and Replace additionally requires a freshly validated backup. Failed/uncertain execution
  outcomes and any partial rollback failure remain recovery-required.
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
