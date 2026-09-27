# Capacity preflight

Phase 4.5 adds a conservative, read-only capacity gate between Preview and Backup. Its purpose is to
reject obvious insufficient-space conditions before destructive migration starts. It does not predict
exact physical allocation.

## Estimate

The reviewed `MigrationPlan` is the source of truth. Excluded, missing-source, unresolved, blocked, and
Skip entries contribute no bytes.

- Copy bytes: source logical bytes for `ReadyToCopy` entries.
- Replace write bytes: source logical bytes for `ReadyToReplace` entries.
- Backup bytes: current destination logical bytes for `ReadyToReplace` entries.

The estimator never subtracts space that Replace may later release. Regular-file default-stream lengths
are summed with checked 64-bit arithmetic. Directory metadata, ACLs, alternate data streams, NTFS cluster
rounding, sparse allocation, compression, deduplication, and journal overhead are not measured.

Each required volume receives a reserve equal to 5% of logical bytes, bounded to a minimum of 64 MiB and
a maximum of 1 GiB. The minimum also reserves capacity for the durable execution journal when no backup
payload is required.

## Volumes

Infrastructure opens the selected directory through the existing handle-relative, no-follow traversal.
It derives the canonical volume GUID identity from the held handle and obtains caller-available free bytes
from Windows. This avoids drive-letter-only comparisons and accounts for aliases such as SUBST.

For separate volumes, destination writes and workspace backup are checked independently, each with its own
reserve. For a shared volume, destination writes and backup bytes are added first and one reserve is added
to the combined total. The more conservative of the two free-space observations is used.

## Fail-safe behavior

Missing or inaccessible entries, kind changes, reparse points, invalid roots, unavailable volume data,
negative/impossible values, arithmetic overflow, and cancellation never yield `Ready`. Preview remains
available because capacity does not alter the reviewed plan or migration outcome.

The workflow stores capacity evidence with the exact safety-workspace string. Changing source,
destination, plan choices, Preview, or workspace clears that evidence. Backup preparation requires a
`Ready` result for the same workspace. Execution still performs its existing live revalidation; a later
disk-full or IO failure follows the existing typed failure and recovery contracts.
