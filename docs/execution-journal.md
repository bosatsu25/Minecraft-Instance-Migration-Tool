# Execution journal and rollback contract

## Current status

Phase 3.3 defines the recovery model **before** migration Execute exists.

It is intentionally split into two immutable artifacts:

```text
Ready MigrationPlan
       |
       v
ExecutionJournalDraft (schema v1)
       |
       | future Execute records outcomes/evidence
       v
ExecutionJournalSnapshot
       |
       v
RollbackPlanPolicy
       |
       +-- NotRequired
       +-- Ready
       +-- RecoveryRequired
       +-- Blocked
```

No journal file is persisted yet and no destination mutation occurs in this phase.

## Journal draft

Only reviewed write intents enter the journal:

- `ReadyToCopy` -> Copy
- `ReadyToReplace` -> Replace

Entries keep deterministic sequence numbers in MigrationPlan order. Skip, NoSource, Excluded, and
other non-write entries do not become execution steps.

A non-ready MigrationPlan blocks journal creation. Manually constructed malformed write intent also
fails closed.

## Runtime snapshot contract

The future executor must report every planned step with the same sequence/name/operation and one of:

- `NotStarted`: no mutation was attempted;
- `Applied`: the intended mutation completed and has a post-write content fingerprint;
- `Failed`: execution reported failure, so partial mutation cannot be ruled out;
- `Uncertain`: the process cannot prove whether mutation completed.

The snapshot is a logical contract, not yet a durable persistence format.

An applied fingerprint contains no path. It records file count, directory count, total bytes, and
SHA-256. Future execution/verification must define how this exact fingerprint is produced for each
written top-level entry.

## Rollback rules

Rollback is planned in reverse execution order.

| Execution evidence | Rollback requirement |
| --- | --- |
| NotStarted | no action |
| Applied Copy + fingerprint | delete the created entry only if current state still matches that fingerprint |
| Applied Replace + fingerprint + currently valid backup | restore from backup only if current state still matches that fingerprint |
| Applied without fingerprint | manual recovery |
| Replace without currently valid backup | manual recovery |
| Failed / Uncertain | manual recovery |
| schema / structure mismatch | rollback blocked |

The current-state fingerprint check is a mandatory guard for future rollback IO. It prevents rollback
from deleting or replacing data that the user or another process changed after migration.

## Durability requirements before Execute

A future Infrastructure journal must satisfy at least:

1. append/transition evidence is persisted durably before the workflow advances;
2. the next destructive step never begins until the previous step's outcome/evidence is durable;
3. restart/crash recovery can distinguish NotStarted, Applied, Failed, and Uncertain without guessing;
4. journal records contain no unnecessary private absolute paths or payloads;
5. a malformed or incomplete journal fails closed;
6. the execution journal is bound to the reviewed MigrationPlan and validated backup evidence;
7. rollback attempts and their own verification results are journaled rather than inferred.

Until those properties have integration and failure-injection tests, Phase 3.3 must not be described
as crash-safe execution or implemented rollback.


## Phase 3.4 durable storage format

The logical contract is now persisted on Windows as a create-only JSONL file named
`mim-journal-{journalId}.jsonl` under an explicit existing local journal parent.

The file starts with one Header record that binds schema v1 and the exact ordered
`ExecutionJournalDraft`. Later records are append-only logical transitions:

```text
Header
Started(0)     -- durable before future mutation 0
Applied(0)     -- includes post-write fingerprint
Started(1)
Failed(1)
```

Each JSON line wraps a canonical payload, a SHA-256 checksum, and a pointer to the previous line's
checksum. The chain detects truncation/reordering/corruption that does not also rewrite the chain.
It is not cryptographic authentication against an attacker who can rewrite the file.

### Durability / crash interpretation

A transition is acknowledged only after its complete JSON line plus newline has been flushed with
`Flush(flushToDisk: true)`.

An unterminated final line is treated as an unacknowledged torn tail. Read-only Load ignores it. Before
a later transition is appended, the storage truncates that tail back to the last acknowledged newline.

This creates the intended recovery semantics:

| Durable records | Recovered outcome |
| --- | --- |
| Header only | NotStarted |
| Started | Uncertain |
| Started + Applied(fingerprint) | Applied |
| Started + Failed | Failed |
| Started + torn Applied tail | Uncertain |
| torn Started tail after prior record | prior acknowledged state |

A complete malformed line or checksum/state-machine mismatch is not ignored; the journal is Invalid.

### Ordering rule

Step N may start only when every step before N is durably Applied and all later steps are still
NotStarted. Once a step is Failed or remains Uncertain, later steps cannot start through this storage.

This rule is deliberately stricter than merely recording events: it gives the future executor a storage
barrier between destructive operations.

### Path / privacy boundary

The journal parent must be an existing absolute local-drive directory. It is resolved component-by-component
with no-follow semantics, and a reparse point fails closed.

Absolute source, destination, backup, and journal-parent paths are not serialized into the journal.
The caller receives the journal path only as an in-memory `ExecutionJournalReference`.

Phase 3.4 is not migration Execute. The next workflow must combine live inspection revalidation,
completed-backup revalidation, durable Started, actual mutation, post-write fingerprinting, and durable
terminal evidence in that order.


## Phase 3.5 orchestration binding

The durable journal protocol is now bound to an Application execution orchestrator.

For each draft step, the orchestrator revalidates the live source/destination observations. Replace
also revalidates the completed backup artifact. Only after those checks pass may it request durable
`Started`.

The future mutation adapter is called only after `Started` succeeds. A separate post-write verifier
must then produce the `ExecutionContentFingerprint`; only that independently produced fingerprint may
be written into durable `Applied`.

If mutation or verification fails after Started, the orchestrator attempts durable `Failed`. If the
terminal journal update itself fails, the operation is RecoveryRequired and restart sees the durable
Started as Uncertain.

Phase 3.5 intentionally leaves `IExecutionMutationPort` and `IExecutionPostWriteVerifier` without
production implementations. This keeps destructive filesystem behavior out until handle-safe Copy /
Replace and independent verification can be tested together on owned fixtures.


## Phase 3.6 production mutation binding

The Phase 3.5 mutation and verifier ports now have Windows implementations.

The mutation adapter is called only after the orchestrator has persisted Started. It reopens source
and destination roots with handle-relative no-follow rules, rechecks the top-level reviewed kind/state,
and performs exactly one Copy or Replace step.

The post-write verifier does not trust the mutation return value. It independently reopens both roots,
computes source/destination/source fingerprints, and only returns Verified when the source was stable
and destination bytes/tree structure match it.

Only that verifier-produced fingerprint is eligible for durable Applied. If mutation or verification
fails, the orchestrator persists Failed where possible and returns RecoveryRequired.

Rollback IO remains separate work and must use the journal fingerprint guard before deleting or
restoring any destination content.


## Phase 3.7 rollback binding

The durable execution journal's Applied fingerprint is now an active rollback safety guard.

`RollbackPlanPolicy` propagates the original expected entry kind into each rollback action and keeps
actions in reverse execution order. `RollbackExecutor` refuses automatic work for Blocked or
RecoveryRequired plans.

For DeleteCreatedEntry, Windows rollback storage fingerprints the exact held destination node before
deleting it. If it no longer equals the Applied fingerprint, rollback stops without mutation.

For RestoreFromBackup, the completed backup artifact is revalidated immediately before the action.
The exact held destination node must still equal the Applied fingerprint. The backup entry is then
fingerprinted, the destination is removed, backup content is copied create-only, and both backup and
restored destination are fingerprinted again.

The execution journal is not extended with rollback-attempt records in Phase 3.7. Therefore a failure
after rollback mutation begins is reported as RecoveryRequired and must not be described as resumable
automatic rollback. A later retry must still pass the original fingerprint guard; partial rollback
normally causes that guard to fail closed.
