# Migration rules

Phase 2.1 introduced the legacy selection defaults and explicit top-level conflict intent.
Phase 1 observes the eleven known names. Phase 2 plans explicit selections. Phase 2.1 adds a
Recommended selection preset and typed Skip / Replace decisions for existing destinations.
Phase 4.6 adds the first known nested content exclusion after inspecting the reference implementation.
Compatibility analysis remains out of scope. Merge is the third conflict decision for directory candidates
([ADR 0004](adr/0004-merge-existing-destination-directories.md)).

| Instance-relative candidate | Legacy intent | Recommended preset direction |
| --- | --- | --- |
| `options.txt` | Game/user settings | Candidate |
| `config/` | Mod/user configuration | Candidate, with exclusions |
| `resourcepacks/` | Resource packs | Candidate |
| `shaderpacks/` | Shader packs | Candidate |
| `schematics/` | Schematics | Candidate |
| `saves/` | Worlds | OFF by default; explicit opt-in |
| `screenshots/` | Screenshots | OFF by default; explicit opt-in |
| `XaeroWaypoints/` | Xaero waypoint data | Candidate |
| `XaeroWorldMap/` | Xaero map data | Candidate |
| `itemscroller/` | Item Scroller user data | Candidate |
| `g4mespeed/` | g4mespeed user data | Candidate |

Phase 2 treats a selected source that is present with the expected kind and a missing destination
as `ReadyToCopy`. Missing sources are explicit no-ops. Phase 2.1 leaves existing destinations
unresolved by default; an explicit Skip becomes a visible no-op and an explicit Replace becomes
`ReadyToReplace` with a backup requirement. Unsafe/unknown observations still block the plan.
This is intent only and performs no IO.

Replace is accepted only when the existing destination has the catalog kind (a file for
`options.txt`, a directory for the others). An explicit Replace of a destination with a different
kind becomes `BlockedDestinationKindMismatch`: the executor can never replace it, so planning refuses it
before any Backup IO or durable `Started` record instead of letting execution fail into
`RecoveryRequired`. Skip remains available for such an entry, and an unresolved decision still shows
`NeedsDecision`.

The known exclusion is **a file whose basename equals `hanemod-client.json` must not be migrated**.
It applies at every depth inside selected directory candidates. Matching is case-insensitive under the
Windows policy. It is an exact basename rule, not a path, glob, pattern, or user-configurable filter.
Similar filenames remain migration content, and directories with that name are not silently skipped.

The rule is Domain-owned and is applied consistently to execution, post-write fingerprints, Backup,
rollback restoration, and capacity measurement. Preview and Report expose a bounded rule summary.
During Replace, a matching destination file is preserved in place and is neither put in the backup nor
restored from it. See the [compatibility matrix](modpacktransfer-compatibility.md) for reference evidence.

## Merge

`Merge` is a third explicit decision for an existing destination directory. Each candidate's merge unit is
Domain-owned in `KnownEntryCatalog`:

| Merge unit | Candidates | Meaning |
| --- | --- | --- |
| File | `config`, `schematics`, `screenshots`, `itemscroller`, `g4mespeed` | each source file is written over the same relative path; destination-only content stays |
| Top-level child | `resourcepacks`, `shaderpacks`, `XaeroWaypoints`, `XaeroWorldMap` | each source child joins or replaces the destination child as a whole |
| Not offered | `options.txt`, `saves` | Merge is an `UnsupportedDecision`; Skip and Replace remain |

A Merge decision on a destination of the catalog kind needs read-only merge inspection evidence for that entry:

- without evidence the entry is `MergeInspectionRequired` and the plan stays `NeedsDecision`;
- clean evidence for the same entry and unit makes it `ReadyToMerge`, which requires a whole-entry backup;
- evidence that reports a kind conflict, a reparse point, or an unreadable tree, or that names another entry
  or unit, makes it `BlockedMergeConflict`.

When choices are applied, the workflow runs the read-only merge inspection for every entry waiting for
evidence and replans; the UI shows the added, overwritten, and kept counts for the selected row. An inspection
that cannot read both trees completely is `Unavailable` and blocks only that Merge.

A Merge of a destination with a different kind is `BlockedDestinationKindMismatch`, as for Replace. Evidence
supplied without a Merge decision is ignored. A `ReadyToMerge` entry becomes a `Merge` journal step only with
clean evidence and directories on both sides; an applied Merge rolls back by guarded `RestoreFromBackup`.

“Candidate” is not a compatibility guarantee. The Recommended preset now selects every known
candidate except `saves` and `screenshots`, which stay OFF until explicitly selected.
Custom explicit selection remains available. The known content exclusion takes precedence over inclusion
and presets.

## Remaining decisions

- Missing candidates, unreadable entries, empty directories, and partial inspection.
- Merge semantics and their backup/rollback requirements; Skip and Replace intent are now explicit,
  with Replace marked as backup-requiring. There is still no silent overwrite.
  [ADR 0004](adr/0004-merge-existing-destination-directories.md) proposes Merge with a per-candidate merge unit.
- Any future exclusion needs explicit scope, precedence, and deterministic ordering.
- Source/destination containment and overlap, junctions/symlinks, stale plans, and concurrent changes.
- Large worlds/screenshots, free space, cancellation, and bounded diagnostics.
- Cross-version configuration and mod-data incompatibility: copying does not prove compatibility.

Rules belong in Domain and must be testable without UI or filesystem access.
Application applies them to Inspector observations; Infrastructure performs authorized IO.
See [architecture](architecture.md) for the full planned workflow and [testing](testing.md)
for the high-risk verification requirements.
