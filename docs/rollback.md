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

If the process disappears after Started but before a terminal record is durable, reload maps that action
to `Uncertain`. This is the critical crash fact that Phase 3.7 could not persist.

### Deliberate limitation: diagnosis is durable, automatic resume is not

Phase 3.8 records which rollback actions are NotStarted, Applied, GuardRejected, Failed, or Uncertain.
It does **not** automatically resume an interrupted rollback.

A durable Uncertain means mutation may or may not have completed. The original execution fingerprint guard
usually prevents blind replay once state is partial, but Phase 3.8 does not infer success from filesystem
appearance alone.

A future recovery UX/policy may inspect this journal and guide manual recovery or define a stronger
independently-verifiable resume protocol.
