# ADR 0004: Merge into existing destination directories

- Status: Accepted (owner decisions recorded below, 2026-10-29)
- Date: 2026-10-29
- Related: [migration rules](../migration-rules.md), [ModPackTransfer compatibility](../modpacktransfer-compatibility.md),
  [backup](../backup.md), [execution journal](../execution-journal.md), [rollback](../rollback.md)

## Context

The reference tool (ModPackTransfer) copies every selected directory with `File.Copy(..., overwrite: true)`.
For a destination directory that already exists, that is an **overlay**: files present in both instances are
overwritten by the old instance, and files that exist only in the new instance stay.

v1.0.0 offers only Skip or Replace for an existing destination. Replace deletes the destination entry and copies
the source entry, so it is not the reference behavior. The compatibility matrix listed "Overwrite" as covered;
this change corrects that row to "Intentionally changed".

The difference matters for the main use case, moving to a new mod-pack version. Mod packs ship `config/`, so
`config` is a destination conflict in practically every migration:

- **Skip** keeps the new pack's configuration and loses every setting the member changed in the old pack.
- **Replace** restores the member's old configuration, but also moves into the backup every configuration file
  that the new pack added, for example tuned settings for newly added mods. Those mods then regenerate their
  own defaults, and the pack author's values are lost without a visible error.
- **Overlay** (the reference) keeps the new files and applies the member's old values to files present in both.

Overlay is not safe everywhere. A file-level overlay of `saves/` mixes region files of two different worlds
that share a folder name, which corrupts both. The unit of merge therefore has to be chosen per candidate.

While reviewing the Replace path for this ADR, one existing gap was found. Before Started, the orchestrator
revalidates top-level kinds and the integrity of the backup artifact, but it never proves that the **current
destination still equals the backed-up destination**. A change made after Backup, for example by a launcher
writing into the instance, would be deleted by Replace and would not be in the backup. Merge depends on the
same property, so this ADR fixes it for both operations.

## Decision

### 1. A third, explicit conflict decision

`DestinationConflictDecision.Merge` is added. It is offered only for directory candidates whose catalog entry
defines a merge unit (section 2). `options.txt` keeps Skip / Replace; a key-level merge of game options is a
different, format-aware problem and is out of scope.

Decisions stay explicit. No preset, including Recommended, chooses Merge, Replace, or Skip on the member's
behalf. An unresolved conflict still blocks the plan.

### 2. Merge unit per candidate (Domain-owned)

`KnownEntryCatalog` gains a merge unit for each directory candidate:

| Candidate | Merge | Unit | Reason |
| --- | --- | --- | --- |
| `options.txt` | not offered | — | Single file; Replace already overwrites it. |
| `config` | offered | File | Reference behavior; keeps configuration the new pack adds. |
| `schematics` | offered | File | Subfolders are the member's categories, not indivisible units. |
| `screenshots` | offered | File | Flat image collection. |
| `itemscroller` | offered | File | Small per-user data. |
| `g4mespeed` | offered | File | Small per-user data. |
| `resourcepacks` | offered | Top-level child | An unpacked pack folder is one unit; mixing two versions leaves stale files. |
| `shaderpacks` | offered | Top-level child | Same as `resourcepacks`. |
| `saves` | held back | (Top-level child when offered) | A world folder is one unit; file-level mixing of two worlds corrupts them. Not offered in the first Merge release (decision 3). |
| `XaeroWaypoints` | offered | Top-level child | Treated as one unit per world or server key (assumption, see Open questions). |
| `XaeroWorldMap` | offered | Top-level child | Same as `XaeroWaypoints` (assumption). |

Semantics:

- **File unit.** For each source directory, the destination directory at the same relative path is used if it
  exists and created if it does not. For each source file, a missing destination file is created; an existing
  one is replaced (deleted by handle, then created from the source). Destination content without a source
  counterpart is not touched.
- **Top-level-child unit.** Each child of the source entry is copied if the destination has no child of that
  name, or replaces the destination child as a whole (today's Replace, applied to the child) if it has one.
  Destination-only children are not touched.
- **Names** match case-insensitively, as everywhere else in the Windows path policy. A replaced file or child
  takes the source spelling; an existing directory that is only descended into keeps the destination spelling.
- **Kind conflicts** (a file on one side and a directory on the other at a compared path) are not mergeable.
- **Reparse points** anywhere in either compared tree block, as they do today.
- **`hanemod-client.json`**: never written from the source and never deleted at the destination, at every depth.

### 3. Read-only merge inspection before the plan is ready

Choosing Merge triggers a read-only comparison of that entry's source and destination trees. It reports bounded
counts only: units added, units replaced, units kept, logical bytes to write, kind conflicts, and reparse points.
No relative or absolute path leaves the inspection.

- Kind conflicts or reparse points make the entry `BlockedMergeConflict`. Skip and Replace remain available.
- A clean inspection makes the entry `ReadyToMerge`.
- The inspection is evidence bound to the reviewed plan, like capacity evidence: changing roots, selections,
  decisions, or Preview clears it.
- Preview, the execution confirmation, and Report show the counts.

### 4. Reuse the existing Backup, capacity, and rollback contracts unchanged

| Stage | Replace (today) | Merge |
| --- | --- | --- |
| Plan disposition | `ReadyToReplace` | `ReadyToMerge`, or `BlockedMergeConflict` |
| Backup | whole destination entry | whole destination entry (same `BackupPlan` shape) |
| Capacity | writes = source bytes; backup = destination bytes | same formula; never subtracts overwritten bytes |
| Journal operation | `Replace` | `Merge`; record shape unchanged |
| Mutation | delete entry, copy source | overlay by the entry's merge unit |
| Verification | destination equals source | destination equals overlay(validated backup, source) |
| Applied fingerprint | whole destination entry | whole destination entry |
| Rollback | `RestoreFromBackup`, guarded by the Applied fingerprint | identical |

Backing up the whole entry costs as much space as Replace already does. In exchange, restore, backup validation,
capacity estimation, and the rollback guard need no new destructive code. A smaller delta backup is listed
under Alternatives.

### 5. Independent verification against an expected overlay

The post-write verifier does not trust the mutation. For a Merge step it streams three inputs:

- the validated backup entry (the pre-merge destination);
- the source entry;
- the current destination entry.

It computes the expected overlay of backup and source by the same Domain-owned unit rule and compares it with the
destination's bytes and structure. Only a match produces the Applied fingerprint, which is the fingerprint of the
whole destination entry. This one check proves both that every source unit was written and that every kept unit
is byte-identical to its pre-merge state.

The verifier port gains access to the backup root for Merge steps.

### 6. Pre-state guard for Replace and Merge

Before Started, live validation proves that the current destination entry's fingerprint equals the validated
backup's fingerprint for that entry. A mismatch is `DestinationChangedSinceBackup` and stops before any durable
record or write.

Inside the mutation, the destination tree is held with no-follow handles, as rollback already does, and the
same comparison is repeated before the first write. A mismatch there is a race inside a narrow window; it fails
closed as `Failed`, and the untouched backup remains the recovery source.

This applies to Replace as well, and closes the gap described in Context. It ships before Merge, as its own
change; the Replace part is implemented (see [execution journal](../execution-journal.md#replace-pre-state-guard)).

### 7. Journals and compatibility

- `ExecutionOperationKind.Merge` is added. Journal and rollback-attempt record shapes, schema numbers, and the
  fingerprint encoding are unchanged, so journals written by v1.0.0 still load and roll back.
- A v1.0.0 build that opens a journal containing `Merge` fails closed: the header no longer matches any draft it
  can build, so the journal is reported invalid and no automatic rollback starts.
- The rollback-attempt storage accepts `RestoreFromBackup` for `Replace` or `Merge`.

## Alternatives considered

| Alternative | Why not now |
| --- | --- |
| Keep Skip / Replace only | Loses the reference tool's main use case and silently drops pack-provided configuration on Replace. |
| File-level overlay for every candidate (exact reference behavior) | Corrupts worlds and mixes unpacked resource-pack versions. |
| Delta backup (only overwritten files) plus a created-path list | Smaller backups, but needs a new artifact format, a new restore algorithm, and per-file journal entries. Revisit if whole-entry backups of large `saves` prove impractical. |
| Per-file decisions in the UI | Maximum control, but too heavy for members. Counts and the unit rule cover the common case. |
| Merge chosen automatically for `config` | Contradicts the no-silent-overwrite rule and the no-default-on file-changing automation rule. |

## Consequences

- Members get the reference behavior for `config` and similar folders, with backup, verification, and rollback,
  and with a safer unit for worlds and packs.
- Merge needs the same backup space as Replace. A large `saves` merge therefore needs a backup of the whole
  destination `saves`.
- Besides Backup, Merge reads the entry up to four more times: inspection, pre-state guard, mutation, and
  verification.
- The pre-state guard adds reads of the destination entry and its backup entry before Started, and one more
  destination read under held handles, but removes a silent data-loss window.
- Infrastructure gains two new pieces, an overlay writer and an overlay verifier. Both are built on the existing
  `WindowsExecutionTree` primitives; no new P/Invoke surface is expected.
- The compatibility matrix marks Overwrite as "Intentionally changed" until Merge ships, then as covered by Merge.
- Copy success still does not prove mod or version compatibility.

## Delivery plan

Each step is its own pull request with its own tests, and each step keeps the product shippable:

1. **Pre-state guard** for Replace (section 6). Independent of Merge. Implemented.
2. **Domain**: decision, dispositions, catalog merge units, and plan, preview, backup, journal, live-validation,
   and rollback policies. Merge is not selectable in the UI yet. Implemented; Application already routes a
   Merge step through backup validation and the pre-state proof, which refuses it until step 3.
3. **Infrastructure**: merge inspection, overlay writer, overlay verifier, and rollback-attempt acceptance, with
   integration tests on owned fixtures.
4. **Application and App**: three-way choice, counts in Preview, confirmation, and Report, Japanese and English
   text, documentation, and a FlaUI Merge smoke test.

## Verification strategy

- **Decision table**: candidate kind × merge unit × destination state (missing, same kind, kind mismatch,
  reparse) × decision (Unresolved, Skip, Merge, Replace) → disposition. Includes Merge on `options.txt`, which
  must be rejected.
- **Equivalence partitions and boundaries for overlay**:
  - empty source or destination;
  - source-only, destination-only, and shared units;
  - shared files with identical bytes, different bytes, sizes 0 and 1, and the copy-chunk boundary;
  - names that differ only in case;
  - `hanemod-client.json` at depth 0, 1, and n on either side;
  - kind conflicts at depth 1 and n;
  - a reparse point at depth n on either side.
- **Golden expected-overlay fingerprints**, computed independently from the documented unit rule, for a fixed
  fixture per merge unit.
- **Negative controls for the verifier**: a dropped kept file, an unwritten source file, a modified kept file,
  and a wrong unit applied must each fail verification.
- **State transitions**: the Merge step through NotStarted → Started → Applied / Failed / Uncertain, and the
  rollback-attempt journal for a Merge restore.
- **Failure injection**: cancellation or an IO failure after k units are written must yield Failed and
  RecoveryRequired. A guarded restore from the backup must then reproduce the pre-merge fingerprint exactly.
- **Pre-state guard**: a destination edit after Backup must be refused before Started for both Replace and Merge.
- **Regression**: the existing Copy and Replace suites and the golden fingerprint values stay unchanged.

## Owner decisions

The owner accepted the recommendations on 2026-10-29:

1. The merge-unit table above is approved.
2. Xaero's folder layout still has to be checked against the members' real instances. Implementation proceeds
   with Top-level child for both Xaero candidates; if newer Xaero versions store data elsewhere, the candidates
   themselves change in a separate decision.
3. Merge is **not** offered for `saves` in the first Merge release. Its catalog unit is "not offered" until a
   member has tried Merge on a copy of real worlds and whole-entry backups of large world folders are judged
   acceptable.
4. Preview, confirmation, and Report show counts only. Relative names are not shown and never enter the journal
   or Report.
