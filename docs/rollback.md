# Guarded rollback

## Scope

Phase 3.7 implements automatic rollback IO only for rollback plans already proven `Ready`.

The intended flow is:

```text
Execution journal snapshot
        |
        v
RollbackPlanPolicy
        |
        v
RollbackExecutor
        |
        +-- DeleteCreatedEntry
        |
        +-- RestoreFromBackup
```

`Failed` or `Uncertain` execution outcomes never become automatic rollback actions.

## Mandatory destination guard

Every automatic action carries the post-write `ExecutionContentFingerprint` recorded after Execute.

Rollback opens the current destination entry and every descendant with no-follow semantics, retains
those handles, and fingerprints that held tree. The retained handles deny writes and deletes by other
handles until the guarded deletion finishes. A nested entry that cannot be held with delete access
rejects the action before any deletion.
If the current content or structure no longer matches journal evidence, rollback does nothing to that
entry. This protects edits made after migration.

For a Copy rollback:

```text
current destination fingerprint
        |
        +-- mismatch -> GuardRejected
        |
        v
delete the same held tree, from leaves to root
        |
        v
verify top-level name is absent
```

## Restore from backup

Replace rollback has two independent prerequisites:

1. Application revalidates the completed backup artifact against the current `BackupPlan`.
2. Infrastructure pins the backup payload and metadata, revalidates the complete artifact under
   those handles, and proves the current destination still matches the execution fingerprint.

Only then does restore begin:

```text
validated backup artifact
        +
destination matches Applied fingerprint
        |
        v
hold every planned backup entry and descendant; compare their aggregate fingerprint
to the validator's evidence
        |
        v
delete guarded destination
        |
        v
copy from the retained backup handles, create-only
        |
        v
fingerprint restored destination
        |
        v
fingerprint the same retained backup handles again
        |
        +-- stable backup == destination -> Applied
        +-- otherwise -> RecoveryRequired
```

Backup and destination roots are rejected when they overlap lexically or through canonical handle
paths. Reparse points are never intentionally followed.

## Ordering and cancellation

`RollbackPlan` is already ordered in reverse execution order. `RollbackExecutor` preserves that
order.

Cancellation is checked again after backup validation and before storage starts. Once an individual rollback storage action begins,
Application passes a non-cancelled token so cancellation cannot intentionally interrupt safety
bookkeeping halfway through that destructive repair.

If cancellation or a guard failure occurs after an earlier rollback action already completed, the
overall result is `RecoveryRequired`, not `Blocked` or `Cancelled`, because the destination is now
partially rolled back.

## Failure model

Before mutation, an inability to prove safety is a guard rejection. Examples include:

- destination missing or changed;
- current fingerprint mismatch;
- invalid or missing backup;
- backup/destination overlap;
- reparse point;
- unsupported path or access failure.

After mutation starts, any failure is `RecoveryRequired`. The code never reports a partially completed
rollback as successful.

## Phase 3.8 durable rollback-attempt journal

Automatic rollback now creates a separate `mim-rollback-{guid}.jsonl` artifact before the first action.
The journal is bound to the exact ordered RollbackPlan and contains no absolute migration/backup paths.

Each action follows:

```text
NotStarted
   |
   v
durable Started
   |
   v
guarded rollback IO
   |
   +-- Applied       -> durable Applied
   +-- GuardRejected -> durable GuardRejected
   +-- partial/error -> durable Failed
```

The journal is append-only at the logical level, checksum-chained, and flushed to disk before an
acknowledged transition returns. A later action cannot start unless every earlier action is durably Applied.

If the process disappears and reload finds Started without a complete valid terminal record, that action
is `Uncertain`. This is the critical crash fact that Phase 3.7 could not persist.

### Crash windows and recovered evidence

Load uses only complete, validated newline-terminated records. A complete malformed record fails closed;
only an unterminated final tail may be ignored and removed before a subsequent append.

| Process-stop point | Evidence observed after reopen |
| --- | --- |
| Before creation | No attempt artifact |
| During header creation | Missing/incomplete header is invalid; a complete valid header yields NotStarted |
| After header, before Started | NotStarted |
| During Started append | Torn tail yields NotStarted; a complete valid Started yields Uncertain |
| After durable Started, before mutation | Uncertain |
| During mutation | Uncertain |
| After mutation, before terminal | Uncertain |
| During terminal append | Torn terminal leaves Uncertain; a complete valid terminal supplies its recorded outcome |
| After terminal durability | Applied, GuardRejected, or Failed, as recorded |
| Between actions | Prior terminals plus NotStarted for remaining actions; partial Applied requires recovery |

A complete record can survive even if its caller never received an acknowledgement. Load reports the
validated bytes present; it cannot establish whether an acknowledgement reached the caller. Started
is acknowledged only after `Flush(flushToDisk: true)`, before storage is invoked. Terminal persistence
failure always returns RecoveryRequired. Cancellation after durable Started does not interrupt the
current action or its terminal bookkeeping.

Schema v1 rejects unknown or duplicate JSON properties and missing required constructor fields.
Header entries must match every ordered plan safety field. Entry names are validated as single Windows
names before serialization, so caller-supplied absolute paths or path fragments cannot enter the journal.
Checksums detect corruption and inconsistent history; they do not authenticate a journal against an actor
who can rewrite the complete artifact and recompute its chain.

Tests cut each header/Started/terminal record at every byte boundary in an ASCII fixture and reopen it,
exercise corrupt complete records without truncating them, and reload real guarded rollback outcomes.
These are deterministic process-interruption models, not a hardware power-loss test. Actual durability
still depends on Windows and the storage device honoring flushes.

### Phase 4.3 recovery diagnosis and UI authorization

Application now reloads the execution journal, revalidates a backup for Applied Replace evidence, and
creates the existing Domain rollback plan before the UI enables rollback. Diagnosis reports typed
execution counts, candidate counts, backup need, and one of RollbackAvailable, NotRequired,
ManualRecoveryRequired, Blocked, or Cancelled. WPF does not parse journal records or reproduce guards.

Rollback requires explicit user confirmation and then uses the existing `RollbackExecutor`. After the
attempt, Application reloads the attempt journal and reports Applied, GuardRejected, Failed, or Uncertain
from durable evidence. A missing/unreadable terminal record is conservatively Uncertain. GuardRejected is
presented as a safe refusal that did not modify the current destination.

### Deliberate limitation: diagnosis is durable, automatic resume is not

Phase 3.8 records which rollback actions are NotStarted, Applied, GuardRejected, Failed, or Uncertain.
It does **not** automatically resume an interrupted rollback.

A durable Uncertain means mutation may or may not have completed. The original execution fingerprint guard
usually prevents blind replay once state is partial, but Phase 3.8 does not infer success from filesystem
appearance alone.

A future recovery UX/policy may inspect this journal and guide manual recovery or define a stronger
independently-verifiable resume protocol.
