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

## Deliberate limitation: rollback is not resumable yet

Phase 3.7 does not add a durable rollback-attempt journal.

A process crash or IO failure during recursive delete or restore can leave partial rollback state.
The original execution journal remains the source of evidence, but it does not record which rollback
sub-actions completed.

A later automatic attempt must pass the original post-write fingerprint guard again. Partial rollback
normally makes that fingerprint differ, causing a safe stop and manual recovery rather than a blind
retry.

A future phase may add durable rollback-attempt records before exposing recovery controls broadly in UI.
