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

## Phase 1.6: WPF UI smoke

The solution test command remains the deterministic core gate. The dedicated
`tests/MinecraftInstanceMigration.UiTests` project uses xUnit v3 and FlaUI.UIA3 5.0.0
(FlaUI.Core 5.0.0 transitively). It is separate from the solution so that core tests
never launch a desktop process. Run it explicitly on Windows with the pinned SDK:
The selected package version is the current stable NuGet release checked during this
phase ([FlaUI.UIA3 5.0.0](https://www.nuget.org/packages/FlaUI.UIA3),
[FlaUI repository](https://github.com/FlaUI/FlaUI)).

```powershell
dotnet restore tests/MinecraftInstanceMigration.UiTests/MinecraftInstanceMigration.UiTests.csproj
dotnet test tests/MinecraftInstanceMigration.UiTests/MinecraftInstanceMigration.UiTests.csproj --configuration Release --no-restore
```

The three smoke checks launch the real WPF executable through UIA3: startup and clean
shutdown, known entry cells, and a visible expected/actual kind mismatch. Input uses
the application's path field, so tests do not depend on native folder-picker timing.
Each fixture is unique and owned by the test; before/after snapshots check relative
names, bytes, attributes, and last-write timestamps. Tests serialize only their UI
collection and always attempt to close/kill their own app process on exit.
UI waits depend on a bounded condition, never a fixed sleep. The UI workflow job is
separate from `verify`; check its actual run before claiming hosted UI support.

During implementation, `InspectKnownFixture` was temporarily changed to expect
an impossible state. It failed with an assertion; after restoration it passed. The
invalid assertion was not committed. This demonstrates that the UI check can go RED
when rendered data differs from expectations.

For a manual check when interactive automation is unavailable:

1. Run the app on a Windows desktop.
2. Select or type an owned test folder containing `options.txt`, `config/`, and `saves/`.
3. Inspect and confirm the root is Directory, the three known states match, and an
   absent known entry is Missing. Confirm the folder's contents and timestamps stay unchanged.
4. In a separate owned folder create `config` as a file, inspect, and confirm the
   table shows expected Directory, observed File, and a false match.
5. Close the app and remove only the owned test folders.

Future candidates only: FsCheck may help when path/plan/rule combinations become broad
enough for property-based tests. Stryker.NET may help after safety decisions for rules,
planning, conflicts, backup, rollback, and verification exist. Neither is installed.
No coverage-percentage gate is defined; behavior and failure-path evidence remain primary.


## Phase 2: Migration Planner core

Phase 2 is pure Domain/Application logic and adds no filesystem writes or UI planner flow.

Coverage includes:

- one shared ordered `KnownEntryCatalog` used by both Inspector and Planner;
- selected present source + missing destination => `ReadyToCopy`;
- selected missing source => explicit `SourceMissing` no-op;
- existing destination => `DestinationConflict` / `NeedsDecision`, without guessing replace/merge/skip;
- source kind mismatch, reparse point, inaccessible/unavailable/invalid observations => blocked;
- destination reparse point or indeterminate observation => blocked;
- non-directory source/destination roots => blocked;
- unknown/case-different selection names => preserved and blocked rather than silently ignored;
- incomplete observations on unselected candidates do not block a selected safe candidate;
- blockers outrank conflicts in aggregate plan status;
- duplicate selections do not duplicate plan entries;
- missing/duplicate observations are blocked instead of guessed;
- Application exposes the Domain plan without filesystem access.

The Planner deliberately has no path containment, collision resolution, preset, exclusion,
size/hash, backup, or executor behavior. Those need their own acceptance criteria and tests.
Because the state space is still eleven fixed candidates with explicit enum states, table-driven
xUnit tests remain sufficient; FsCheck remains a future candidate rather than a dependency.


## Phase 2.1: selection preset and conflict intent

Phase 2.1 remains pure Domain/Application logic and performs no filesystem IO.

Coverage includes:

- the Recommended preset selects all known candidates except `saves` and `screenshots`;
- an unresolved existing destination remains `NeedsDecision`;
- explicit Skip becomes a visible no-op and never counts as write-ready;
- explicit Replace becomes `ReadyToReplace` and is marked backup-requiring;
- conflict decisions for unknown entries or unselected entries block the plan;
- stale decisions for entries with no actual destination conflict block instead of being ignored;
- unsupported enum values block and leave the conflict unresolved;
- source safety blockers still win even if the caller supplied Replace intent;
- Application exposes both custom planning and the Domain-owned Recommended preset.

At Phase 2.1 this policy deliberately did not define Merge or the `hanemod-client.json` exclusion scope.
Phase 4.6 resolves the exclusion from inspected reference code; Merge remains outside the product contract.


## Phase 2.2: Preview / Dry Run

Phase 2.2 adds a pure plan-to-preview projection plus a WPF dry-run flow. It still performs no
filesystem writes.

Coverage includes:

- every current `MigrationPlanDisposition` maps to one explicit preview action;
- unsupported future dispositions fail closed instead of being silently reinterpreted;
- preview preserves aggregate plan status, unknown selections, conflict-decision issues, and backup intent;
- copy/replace/skip/no-source/needs-decision/blocked counts reflect the plan snapshot;
- Application previewing uses an existing plan and performs no inspection or IO;
- the Preview ViewModel inspects source then destination, applies the Recommended plan, and exposes the same preview;
- existing destination conflicts are visible as `NeedsDecision`;
- changing either path clears stale preview state;
- cancellation between inspections does not publish a late result;
- errors expose only exception type, not private path-bearing messages;
- hosted FlaUI smoke switches to the Preview tab, generates a preview from owned fixtures, verifies
  visible Copy / Excluded outcomes, and compares source/destination fixture snapshots before and after.

At Phase 2.2 the UI did not edit Skip / Replace decisions; Phase 4.1 adds that editing flow. The UI
still does not report size estimates because the bounded Phase 1 Inspector intentionally reads metadata
only and does not recursively inventory content.
A passing preview is not execution authorization; Backup / Execute must revalidate state.


## Phase 3.0: Backup Preflight

Phase 3.0 is deliberately read-only. It adds Domain/Application backup preparation without an
Infrastructure backup writer.

Coverage includes:

- copy-only plans produce `NotRequired`;
- `ReadyToReplace` produces a `Ready` backup entry;
- multiple replacement entries preserve migration-plan order;
- `NeedsDecision` and `Blocked` migration plans block backup;
- malformed replacement intent whose destination is not File/Directory fails closed;
- a manifest draft can be created only from a ready backup plan;
- the manifest draft is schema-versioned and contains relative known names / kinds / observed states only;
- backup models defensively copy caller collections;
- Application exposes the same deterministic preflight without filesystem access.

No integration filesystem test is claimed for Phase 3.0 because no production filesystem write
exists. Before adding Backup IO, tests must cover owned temporary roots, path overlap/containment,
nested reparse points, cancellation/failure injection, partial-state handling, independent backup
verification, and safe cleanup behavior. Real Minecraft data must never be used as a fixture.


## Phase 3.1: Backup IO foundation

Phase 3.1 is the first production filesystem-write phase. Integration tests use only owned temporary
Windows fixtures and never real Minecraft data.

Coverage includes:

- Application returns `NotRequired`, `InvalidPlan`, or pre-cancelled outcomes without crossing the storage boundary;
- ready backup plans create a manifest draft and delegate exactly once;
- real Infrastructure backup copies a nested directory tree and file payload into a new owned
  `mim-backup-*` root;
- destination fixture bytes remain unchanged by backup;
- completion writes both the ownership marker and `backup-manifest.json`;
- manifest text contains no absolute destination / backup-parent paths;
- returned verification counts bytes/files/directories and exposes a SHA-256 tree fingerprint;
- a nested junction/reparse point fails closed and its target payload is never copied;
- a backup parent inside the destination root is rejected before root creation;
- stale planned kind versus actual destination kind produces `SourceChanged`, preserves the owned
  partial root, and does not write a completion manifest;
- a pre-cancelled Infrastructure request creates no backup root.

The production traversal uses retained directory handles, `NtQueryDirectoryFile`, and relative
`NtCreateFile` opens with no-follow semantics rather than recursive absolute-path enumeration.
Post-copy verification fingerprints source, backup, then source again; a changing source or a
content/structure mismatch fails the operation.

Remaining gaps before Execute: deterministic mid-copy failure injection, restore/rollback behavior,
ACL / alternate-stream / timestamp preservation decisions, and revalidation of a completed backup
immediately before destructive migration writes.


## Phase 3.2: Completed backup revalidation

Phase 3.2 is a read-only verification phase over an already completed backup artifact.

Coverage includes:

- invalid and pre-cancelled requests do not cross the Application storage boundary;
- a completed backup validates successfully and validation leaves the artifact byte-for-byte unchanged;
- payload mutation after backup completion produces `VerificationMismatch`;
- missing completion manifest produces `ManifestInvalid`;
- invalid ownership marker produces `OwnershipMarkerInvalid`;
- unexpected top-level backup content produces `UnexpectedContent`;
- a nested junction injected after backup completion produces `ReparsePoint`;
- a manifest created for a different backup plan is rejected.

The validator reuses the same handle-relative no-follow traversal and tree-fingerprint algorithm as
backup creation. It does not repair, clean up, or rewrite an invalid artifact. A GREEN result is
point-in-time evidence only; Execute still needs destination/source revalidation plus an execution
journal and rollback contract.


## Phase 3.3: Execution journal / rollback contract

Phase 3.3 is pure Domain/Application logic and performs no migration or rollback filesystem writes.

Coverage includes:

- Ready Copy / Replace intents become ordered schema-v1 journal entries;
- a Ready plan with no writes produces `NotRequired`;
- non-ready migration plans block journal creation;
- malformed Copy / Replace intents fail closed;
- journal models defensively copy caller collections;
- an applied Copy becomes fingerprint-guarded `DeleteCreatedEntry`;
- an applied Replace becomes fingerprint-guarded `RestoreFromBackup` only with currently valid backup evidence;
- missing backup evidence, Failed / Uncertain outcomes, or missing post-write fingerprints require manual recovery;
- rollback actions are ordered in reverse execution order;
- journal schema or structure mismatch blocks rollback;
- malformed SHA-256 evidence is rejected;
- the Application adapter translates `BackupArtifactValidationResult.IsValid` into the Domain rollback decision.

No Infrastructure journal writer or rollback adapter exists in this phase. Before Execute, tests must
cover durable journal append/flush semantics, crash/interruption points, destination revalidation,
per-step post-write fingerprinting, and the guarantee that journal evidence is persisted before a
later destructive step begins.


## Phase 3.4: Durable execution journal storage

Phase 3.4 writes only the execution-journal artifact; it still performs no Minecraft migration writes.

Application coverage includes:

- blocked journal drafts never cross the storage boundary;
- pre-cancelled create requests never cross the storage boundary;
- invalid step numbers never cross the storage boundary;
- valid create/start/applied/load operations delegate to the storage port.

Windows Infrastructure coverage includes:

- create-only `mim-journal-{guid}.jsonl` creation under an owned temporary parent;
- header content contains no absolute fixture/journal-parent paths;
- initial reload returns every step as `NotStarted`;
- a durably flushed Started record reloads as `Uncertain` until a terminal record exists;
- Applied fingerprints survive process-style close/reopen;
- an unterminated torn terminal record is ignored on Load and truncated before the next acknowledged append;
- a complete malformed record fails closed;
- checksum mutation fails closed;
- later steps cannot start before prior steps are durably Applied;
- a Failed step prevents later-step execution;
- a journal cannot be reopened against a different draft;
- a reparse-point journal parent is rejected without creating a file in its target;
- pre-cancelled direct Infrastructure create leaves no journal artifact.

The format uses a checksum chain plus strict state-machine validation. Checksums detect accidental or
uncoordinated edits but are not authentication. Crash tests model the critical storage invariant:
an unterminated append was never acknowledged, while a durable Started record without a durable terminal
must recover as Uncertain.

Before Execute is added, the next phase must bind this journal protocol to live source/destination
revalidation and backup revalidation, and must prove by failure injection that no destructive mutation
begins before Started is durable and no later mutation begins before the previous terminal evidence is durable.


## Phase 3.5: Live revalidation / execute orchestration

Phase 3.5 is Domain/Application only and deliberately has no production migration mutation adapter.

Coverage includes:

- unchanged reviewed Copy state passes live validation;
- destination appearance before Copy is rejected;
- source kind/state drift before Replace is rejected;
- unsafe roots are rejected;
- journal-step operation must match the reviewed write intent;
- Application live validation inspects source then destination immediately before a step;
- Replace ordering is exactly journal-create -> live revalidate -> backup revalidate -> durable Started
  -> mutation port -> post-write verify -> durable Applied;
- Copy does not require backup validation;
- live-state drift or invalid backup prevents Started and mutation;
- a failed Started journal write prevents mutation;
- mutation failure attempts durable Failed and returns RecoveryRequired;
- post-write verification failure attempts durable Failed and returns RecoveryRequired;
- failure to persist Applied after mutation returns RecoveryRequired;
- cancellation after one safely Applied step stops before the next Started;
- revalidation failure after an earlier Applied step returns RecoveryRequired rather than pretending
  the entire migration was merely blocked.

The mutation/verifier ports are test doubles only in this phase. The next production phase must add
handle-safe single-entry Copy/Replace IO plus an independent fingerprint verifier and then run the same
ordering against real owned temporary fixtures with deterministic failure injection.


## Phase 3.6: Windows mutation / post-write verification

Phase 3.6 is the first production migration-write adapter. All integration tests use only owned
temporary Windows fixtures.

Coverage includes:

- Copy creates a missing file entry without changing the source;
- Copy refuses an already existing destination instead of overwriting it;
- nested source junctions fail closed and their target payload is not copied;
- Replace recursively removes the reviewed destination tree and copies the source payload;
- nested destination junctions fail closed without following or modifying the junction target;
- equal/ancestor/descendant source/destination roots are rejected before mutation;
- physical overlap through a SUBST alias is rejected after canonical handle-path comparison;
- missing source and destination roots retain side-specific failure classification;
- a journal parent inside either migration root is rejected before journal creation;
- the independent verifier compares source -> destination -> source and records a path-free
  `ExecutionContentFingerprint`;
- destination tampering after mutation produces `VerificationMismatch`;
- full real-port Copy orchestration produces a durable Applied journal record;
- full real-port Replace orchestration first creates/revalidates backup, replaces the destination,
  preserves the backup payload, independently verifies output, and persists Applied;
- a nested source reparse that passes top-level live inspection but fails during mutation produces
  durable Failed recovery evidence.

The mutation adapter rechecks top-level state after durable Started, so a race between Application
live inspection and mutation still fails closed. Output creation is create-only. Replace deletion and
nested traversal use handles and never intentionally follow reparse targets.

This phase does not prove exact NTFS metadata preservation. ACLs, alternate data streams, and complete
timestamps remain outside the payload contract.


## Phase 3.7: Guarded rollback IO

Phase 3.7 performs destructive rollback only on owned temporary Windows fixtures.

Domain/Application coverage includes:

- rollback actions preserve the original expected entry kind;
- NotRequired / RecoveryRequired plans never cross the automatic storage boundary;
- DeleteCreatedEntry does not require backup validation;
- RestoreFromBackup revalidates the completed backup immediately before storage;
- invalid backup blocks restore before destination mutation;
- a first fingerprint guard rejection is Blocked;
- a guard rejection after an earlier rollback action is RecoveryRequired;
- cancellation before rollback performs no storage action;
- cancellation during a successful backup validation still stops before destructive storage;
- cancellation after an earlier applied rollback is RecoveryRequired;
- storage failures after mutation begins remain RecoveryRequired.

Windows integration coverage includes:

- an Applied Copy is deleted only while its current destination fingerprint matches journal evidence;
- editing a copied destination after migration rejects rollback and preserves the edit;
- an Applied Replace can restore the verified backup end-to-end;
- the restored destination is independently fingerprinted against a stable backup;
- tampering with the completed backup blocks restore before destination mutation;
- nested backup reparse points fail closed and their target is never followed;
- retained destination and backup descendant handles deny concurrent writes through the guard and copy;
- nested delete-access denial is rejected before mutation, while a missing backup root has a backup-side failure kind;
- unexpected backup content inserted after earlier validation blocks restore before destination mutation.

Rollback is intentionally not described as transactionally crash-safe. No durable rollback-attempt
journal exists in Phase 3.7. A failure after rollback mutation starts may leave partial state and is
reported as RecoveryRequired. Automatic retry must still satisfy the original post-write fingerprint
guard; it never blindly deletes or restores over user changes.


## Phase 3.8: Durable rollback-attempt journal

Phase 3.8 adds deterministic Application and hosted-Windows coverage for durable rollback evidence.

Application coverage includes:

- NotRequired / RecoveryRequired rollback plans never create an attempt journal;
- a journal is created before any automatic rollback action;
- Restore backup validation completes before durable Started;
- storage is never called when Started cannot be persisted;
- GuardRejected and storage recovery failures receive durable terminal records;
- an Applied storage mutation whose Applied journal write fails returns RecoveryRequired;
- cancellation before the first action creates no attempt;
- cancellation after an earlier durably Applied action returns RecoveryRequired;
- the journal parent is mandatory for automatic rollback.

Windows journal coverage includes:

- path-free create-only header bound to the exact RollbackPlan;
- initial NotStarted recovery;
- durable Started without a terminal record recovers as Uncertain;
- Applied / GuardRejected / Failed terminals survive close/reopen;
- later actions cannot start before earlier Applied;
- an unterminated torn terminal record is ignored and truncated before the next acknowledged append;
- a journal parent inside destination is rejected before artifact creation;
- a different rollback plan cannot reuse an existing attempt journal.

Independent audit regression coverage also includes:

- path-shaped plan entry names are rejected before creating any artifact;
- null header entries, unknown/duplicate JSON properties, invalid checksum links, index gaps,
  duplicated/reordered records and complete malformed JSON fail closed without changing journal bytes;
- each safety field in the ordered plan is bound to the header;
- duplicate starts/terminals and terminals without Started cannot change journal bytes;
- every truncated header/Started/terminal prefix preserves only the prior complete evidence;
- Applied plus NotStarted or GuardRejected remains recovery-required after reload;
- journal placement inside backup and inside either protected root through SUBST aliases is rejected;
- reparse parents and parent replacement with a junction fail closed;
- cancellation after durable Started still completes non-cancellable storage and terminal persistence;
- cancellation during backup validation never starts an action;
- storage exceptions attempt a durable Failed terminal and require recovery.

Real rollback integration reloads the durable attempt after guarded Copy deletion, verified Replace
restore, and a modified-destination rejection, requiring Applied or GuardRejected respectively.
The crash-window table and limits of simulated interruption are documented in [rollback](rollback.md).

Phase 3.8 improves crash diagnosis but does not implement automatic rollback resume. An Uncertain or Failed
action remains recovery-required.


## Phase 4.0: Migration workflow / session

Application workflow tests use ports and controlled stubs; they never access the filesystem. They verify:

- a new session starts at `SelectRoots` and valid roots advance it to `Inspect`;
- inspection publishes source before destination evidence and advances to `ConfigurePlan`;
- plan configuration records selections and conflict decisions before preview;
- a ready preview is the only path to `ReadyForBackup`;
- unresolved conflicts remain at `Preview` and never create a backup plan;
- backup execution must complete or be `NotRequired` before `ReadyForExecution`;
- execution receives the reviewed roots, plan, backup result, and journal parent through its port;
- completed execution reaches `Completed`, while recovery-required execution reaches `RecoveryRequired`;
- cancellation during inspection discards partial observations;
- a `NotRequired` backup reaches `BackupReady` without invoking the backup executor or requiring a path;
- required backup plans reject missing backup parents, while journal-parent validation remains at execution preparation;
- reconfiguring a pre-execution plan copies caller collections and clears preview, backup, parent, and execution evidence;
- session construction and state setters are application-owned, so tests cannot forge a workflow state;
- an operation-canceled exception escaping execution is classified as `RecoveryRequired`.

The workflow does not choose conflict policy, perform IO, expose private exception messages, or implement
automatic resume. Phase 4.1 re-enters `ConfigurePlan` with the same inspected evidence for selection and
conflict editing.


## Phase 4.1: selection / conflict editing UI

App tests verify that Recommended is the initial selection, opt-in/opt-out changes affect only the selected row, Skip / Replace / Clear edit current destination conflicts, Apply choices rebuilds the preview without reinspection, Reset Recommended restores defaults, path changes clear pending choices, and workflow failures do not leak private exception messages or rebuild stale previews.

Hosted WPF smoke coverage waits for user-visible preview rows rather than inferring asynchronous completion
from command enablement or a cached UIA text element. It verifies Copy / Excluded rows and editing controls
for a real destination conflict while source and destination fixtures remain unchanged. The preview grid
contains exactly the eleven known candidates, so row/column virtualization is disabled to expose that bounded
result consistently to keyboard users and UI Automation. Backup / Execute are deliberately not invoked by
Phase 4.1 UI tests.


## Phase 4.2: confirmed execution UI

App tests cover Ready/NeedsDecision/Blocked availability, pending-choice rejection, explicit confirmation,
Copy-only backup bypass, Replace backup ordering, backup failure/cancellation, typed execution failure and
RecoveryRequired results, duplicate execution prevention, input locking, stale-session invalidation,
cancellation propagation, and redaction of private exception messages. Controlled Application ports verify
call order without weakening workflow policy.

The hosted FlaUI smoke uses owned temporary source, destination, safety-workspace, and unrelated fixtures.
It generates a Copy-only Preview, supplies the workspace, opens the dedicated confirmation window, confirms,
waits for the user-visible `Completed` workflow state with bounded retry, and verifies copied bytes. It also
proves the source and unrelated fixture remain unchanged. The real Windows composition root performs journal
creation, live revalidation, mutation, and independent post-write verification; no fixed sleep is used.

## Phase 4.3: recovery diagnosis / guarded rollback UI

Application tests cover durable execution-journal loading, Applied Copy to DeleteCreatedEntry planning,
Replace backup revalidation, invalid-journal fail-closed behavior, manual recovery for Failed / Uncertain
execution evidence, exact diagnosed-plan delegation, and durable Applied / GuardRejected / Failed /
Uncertain rollback-attempt classification. The authorization regression rejects a different request,
a substituted rollback plan, and replay of an already consumed diagnosis before calling the executor.

App tests cover RecoveryRequired visibility, rollback eligibility and blocking, explicit confirmation,
confirmation rejection before the rollback port, result-specific safe wording, duplicate rollback
prevention, input locking, and redaction of exception/path details. These use controlled Application ports;
the existing Infrastructure regression suite continues to exercise real owned fixtures for fingerprint
guards, backup integrity, reparse rejection, checksum chains, torn records, and attempt durability.

The hosted FlaUI suite continues to cover real confirmed Copy execution. Phase 4.3 does not add a
production failure-injection switch solely for UI automation, so recovery exposure is verified at the
ViewModel/Application boundaries rather than weakening production behavior or manufacturing an unstable
destructive smoke path.

## Phase 4.4: Migration Report

Application tests verify every report outcome, exact Preview/action counts, verification and backup
summaries, absence of fake recovery data, full-rollback requirements for Recovered, and fail-closed
handling of missing or inconsistent execution/recovery evidence. Serialization of the report model is
checked for path leakage even when source evidence contains private backup and journal locations.

App tests verify readable Completed and recovery presentations, distinct Recovered / GuardRejected /
Failed / Uncertain wording, safe report-generation failure, absence of workflow commands, publication
after backup/preparation/execution outcomes, and clearing when roots change. Report projection failures
do not modify the workflow session or replace its typed result.

The hosted FlaUI normal path extends the real confirmed Copy-only migration through the Report tab. It
observes Completed, Copy count 1, and successful verification from stable AutomationIds. Recovery report
paths remain at Application/App level because Phase 4.4 adds no production failure injection solely for
UI automation. No report persistence tests exist because Phase 4.4 performs no report filesystem writes.

## Phase 4.5: capacity preflight

Application tests use deterministic size and volume probes. They cover Copy and Replace accounting,
backup bytes, excluded/Skip omission, separate and shared-volume decisions, both insufficient-space paths,
unavailable measurements, reparse classification, cancellation, checked arithmetic, bounded margin, and
workflow invalidation. A Ready preview remains readable when capacity fails, while backup preparation is
blocked until Ready evidence exists for the exact workspace.

Infrastructure tests use owned temporary fixtures for file, zero-byte, multi-file nested directory, missing
entry, nested junction rejection, invalid/reparse roots, canonical volume identity, same-volume identity,
and caller-available free space. They never scan a real Minecraft instance and never follow a reparse target.

App tests cover human-readable summaries, Ready/insufficient/unavailable command state, same-volume notice,
workspace invalidation, private-message redaction, and OneWay read-only bindings. Hosted FlaUI extends the
confirmed Copy-only path with an explicit Check Capacity step and observes `Ready` before Execute using
bounded retry. Disk-full is not manufactured in UI automation; insufficient cases use deterministic ports.

## Phase 4.6: ModPackTransfer compatibility closure

Domain tests pin the ordered eleven-entry All preset, the nine-entry Recommended preset, exact basename
matching, similar-name non-matches, and case-insensitive Windows semantics. Application/App tests verify
that Preview and Report expose the rule summary and that Select All/None do not reinspect roots.

Windows integration tests use owned temporary fixtures to prove that root and nested
`hanemod-client.json` files are omitted while siblings are copied, fingerprints verify the same filtered
payload, backups omit the file, capacity omits its bytes, and Replace plus guarded rollback preserve an
existing destination copy without backing it up or restoring it. Existing reparse, overlap, stale-evidence,
conflict, privacy, and UI smoke suites remain regression gates.

The traceability matrix names the inspected reference commit and links every original behavior to the new
implementation and deterministic tests. A `Missing` row blocks the Phase 4.6 completion assessment.

## Phase 5.0: release hardening

App tests verify assembly-derived product/version information and configuration invariants for the central
version, `win-x64`, self-contained publishing, disabled trimming/AOT/single-file, PDB omission, and the
per-user x64 installer. Existing layer and migration tests remain unchanged.

`Build-Release.ps1` performs a real RID restore/publish, rejects source/test/fixture/debug files, checks
Windows version metadata, launches the published executable, creates the portable ZIP and installer,
checks signature state, writes checksums last, and reopens the ZIP for content verification.
`Test-Installer.ps1` silently installs into a new per-run temporary directory, verifies the executable,
license, and uninstaller, then uninstalls it. It refuses an existing test directory instead of clearing it.
Phase 5.1 still owns clean-machine and real upgrade validation.

Hosted completion requires the ordinary `verify` and `ui-smoke` gates plus the release workflow's
`release-package` job. The package job depends on its own core/UI verification and cannot upload artifacts
after a failed gate. README-only changes remain excluded from these workflows. Trusted tag signing is not
exercised without production credentials and may never run for pull requests.
