# Backup design

## Current status

Phase 3.0 implements **Backup Preflight only**. It performs no filesystem IO.

The backup contract starts from the reviewed `MigrationPlan`; it does not rediscover or reinterpret
migration intent.

```text
MigrationPlan
   |
   v
BackupPlanPolicy
   |
   +-- NotRequired
   +-- Ready ------> BackupManifestDraft (schema v1)
   +-- Blocked
```

Only `ReadyToReplace` entries require backup because they represent existing destination data that
a future Execute phase would replace. `ReadyToCopy` targets a missing destination and therefore has
no prior destination payload to preserve.

## Fail-closed rules

Backup is blocked when the migration plan is not `Ready`. A replacement entry is also blocked when
its destination state is not an observed File or Directory.

A manifest draft can be produced only from a ready backup plan. The draft contains no absolute paths,
user identifiers, file contents, hashes, or timestamps. It is a planning artifact, not proof of backup.

## Why production Backup IO is not in Phase 3.0

A naive recursive copy would introduce privacy and containment risk before the repository has defined
safe nested traversal. String normalization alone is not proof that a path stayed inside the intended
destination root, and checking a reparse point before later opening the same path is vulnerable to
time-of-check/time-of-use changes.

Backup IO therefore remains gated on an explicit design and integration-test phase covering:

1. backup-root ownership and overlap rejection;
2. handle-safe nested traversal;
3. reparse-point and junction rejection/handling at every depth;
4. cancellation and injected failures;
5. partial-backup state and recovery semantics;
6. independent completeness verification;
7. bounded, redacted diagnostics;
8. safe cleanup under concurrent path mutation.

No future Execute implementation may treat a Phase 3.0 preflight or manifest draft as a completed backup.
