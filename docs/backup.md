# Backup design

## Current status

Phase 3.1 implements **Backup IO foundation** on Windows.

The contract is:

```text
MigrationPlan
   |
   v
BackupPlanPolicy
   |
   +-- NotRequired
   +-- Ready ------> BackupExecutor ------> WindowsBackupStorage
   |                                         |
   |                                         +-- owned partial root
   |                                         +-- copy
   |                                         +-- verify
   |                                         +-- completion manifest
   +-- Blocked
```

Only `ReadyToReplace` entries require backup. `ReadyToCopy` targets a missing destination and
therefore has no prior destination payload to preserve.

## Owned backup root

The caller supplies an existing local backup-parent directory. The adapter creates a unique
`mim-backup-{guid}` child and writes `.mim-backup-owner.json` first.

The backup parent may not equal or be inside the destination root. Local-drive paths only are
accepted. Network, relative, device-style, parent-traversal, trailing-dot/space, and reparse-based
paths are rejected or fail closed.

A failed or cancelled backup keeps its owned partial root for diagnosis/recovery. Automatic recursive
cleanup is deliberately absent: deleting a partially written tree safely under concurrent path
mutation requires its own proven cleanup contract.

## Handle-safe traversal

Source traversal does not use recursive absolute-path enumeration. Ancestors are retained as handles.
Directory entries are enumerated through `NtQueryDirectoryFile`; every name is opened relative to
the retained parent handle with `OBJ_DONT_REPARSE` and `FILE_OPEN_REPARSE_POINT`.

Any junction, symbolic link, or other reparse point encountered at any traversed depth stops backup.
The adapter never intentionally follows a reparse target.

Backup output nodes are created relative to the newly owned backup-root handles with create-only
semantics, so existing output is never silently overwritten.

## Verification and completion

After copying all planned replacement entries, the adapter computes a deterministic SHA-256 tree
fingerprint of:

1. the destination entries,
2. the copied backup entries,
3. the destination entries again.

The fingerprint includes relative structure, regular-file default-stream bytes, file/directory
counts, and total bytes. If the two source fingerprints differ, the result is `SourceChanged`.
If source and backup fingerprints differ, the result is `VerificationFailed`.

`backup-manifest.json` is written only after those checks pass. The manifest contains schema version,
known top-level entry metadata, and aggregate verification data; it contains no absolute source,
destination, or backup paths.

Manifest existence means this Backup IO phase completed its copy/verification contract. It does not
authorize migration Execute by itself.

## Cancellation and failure

Cancellation is checked before root creation, between entries, during recursive traversal, during
copy loops, and during verification. If cancellation happens after root creation, the result returns
`Cancelled` with the owned backup-root path and no completion manifest.

Expected filesystem failures are classified without surfacing exception messages that may contain
private paths. Partial roots are retained.

## Deliberate limitations

Phase 3.1 mirrors directory structure and regular-file default-stream bytes. It does not promise an
exact NTFS clone: ACLs, alternate data streams, and full timestamps/metadata are not preserved.

Before Execute / restore work can rely on backup for destructive changes, the project still needs:

1. deterministic mid-copy failure injection tests;
2. restore and rollback semantics;
3. an explicit decision on ACL / alternate-stream / timestamp preservation;
4. completed-backup revalidation immediately before any destructive write;
5. safe partial-root cleanup policy;
6. bounded UI/progress/reporting behavior.

No future Execute implementation may treat a Phase 3.0 preflight or a Phase 3.1 partial root as a
completed backup.


## Completed-artifact revalidation

Phase 3.2 adds a read-only revalidation step for completed backup roots. It exists because the
artifact can be moved, edited, partially deleted, or tampered with after Phase 3.1 completed.

Validation reopens the backup root using the same local-path and no-follow rules, verifies the owner
marker is bound to the root name, parses the completion manifest, checks that its entries exactly
match the current `BackupPlan`, rejects unexpected top-level content, and recomputes the entire
planned tree fingerprint. Any nested reparse point or fingerprint mismatch invalidates the artifact.

The validator never repairs or rewrites the backup. A valid artifact is necessary recovery evidence,
but it is not sufficient to start destructive Execute: the future workflow must still revalidate the
live migration inputs and bind the validated backup to an execution journal.


## Relationship to execution journal / rollback

Phase 3.3 consumes completed-backup validation as recovery evidence. An applied Replace can become an
automatic `RestoreFromBackup` rollback requirement only when backup validation is currently valid.

That requirement is still only a Domain plan. Future rollback IO must revalidate both the backup and
the current destination state against the execution journal's post-write fingerprint before replacing
anything. A stale or edited destination must become recovery-required rather than being overwritten.
