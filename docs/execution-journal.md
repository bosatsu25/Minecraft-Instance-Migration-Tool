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
