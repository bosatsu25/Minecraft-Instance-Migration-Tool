# ModPackTransfer compatibility closure

## Reference

- Repository: [TaichiServer/ModPackTransfer](https://github.com/TaichiServer/ModPackTransfer)
- Inspected commit: [`e174cdac8229f3e061175a36121d55db01961452`](https://github.com/TaichiServer/ModPackTransfer/tree/e174cdac8229f3e061175a36121d55db01961452)
- Runtime/UI: .NET Framework 4.7.2 WinForms ([project lines 8-11](https://github.com/TaichiServer/ModPackTransfer/blob/e174cdac8229f3e061175a36121d55db01961452/FolderCopy/FolderCopy.csproj#L8-L11))
- Behavioral source of truth: [`Form1.cs`](https://github.com/TaichiServer/ModPackTransfer/blob/e174cdac8229f3e061175a36121d55db01961452/FolderCopy/Form1.cs) and [`Form1.Designer.cs`](https://github.com/TaichiServer/ModPackTransfer/blob/e174cdac8229f3e061175a36121d55db01961452/FolderCopy/Form1.Designer.cs)

The inspected project file also references absent `Form2.cs` files. That repository defect is not a
migration feature and is not reproduced here.

The owner's supplied source ZIP was independently inspected in place for member-distribution work.
Its Form1 source contains the same eleven candidates, All/None/Recommended actions, recursive copy,
and exact-basename exclusion. It is used as behavioural reference only and is not copied into the
successor package. The packaged UI regression now exercises all eleven candidates in one real
Copy/Replace workflow and checks the excluded source and retained destination contents.

## Original feature inventory

The code defines 18 user-visible or migration-relevant behaviors:

1. source folder selection;
2. destination folder selection;
3. eleven selectable migration candidates;
4. select all;
5. select none;
6. a nine-entry recommended selection action;
7. `options.txt` copy;
8. recursive directory copy;
9. overwrite of existing files;
10. exact-name `hanemod-client.json` exclusion during recursive directory copy;
11. exclusion at every recursion depth;
12. source/destination final-name warning;
13. no-selection warning;
14. completion dialog;
15. raw exception dialog;
16. directory creation;
17. directory-attribute copying;
18. synchronous execution on the UI event handler.

The reference invokes copy before validating the final directory name or selection
([lines 38-51](https://github.com/TaichiServer/ModPackTransfer/blob/e174cdac8229f3e061175a36121d55db01961452/FolderCopy/Form1.cs#L38-L51)).
It overwrites files unconditionally ([lines 152-163](https://github.com/TaichiServer/ModPackTransfer/blob/e174cdac8229f3e061175a36121d55db01961452/FolderCopy/Form1.cs#L152-L163)),
recurses through every directory ([lines 167-172](https://github.com/TaichiServer/ModPackTransfer/blob/e174cdac8229f3e061175a36121d55db01961452/FolderCopy/Form1.cs#L167-L172)),
and shows raw exception messages ([lines 54-56](https://github.com/TaichiServer/ModPackTransfer/blob/e174cdac8229f3e061175a36121d55db01961452/FolderCopy/Form1.cs#L54-L56)).

## Migration candidate coverage

`KnownEntryCatalog` is the ordered source of truth for all eleven candidates. Inspector, Planner,
Preview, selection editing, Copy/Replace/Skip, capacity preflight, verification, and report projection
consume the same catalog or reviewed plan. Existing catalog, planner, workflow, App, Infrastructure,
and report tests exercise the common pipeline; the catalog-order regression prevents silent drift.

| Candidate | Kind | Recommended | End-to-end disposition support | Status |
| --- | --- | --- | --- | --- |
| `options.txt` | File | On | Include/Exclude, Copy, Replace, Skip, capacity, verify, report | Covered |
| `config` | Directory | On | Include/Exclude, Copy, Replace, Skip, capacity, verify, report | Covered |
| `resourcepacks` | Directory | On | Include/Exclude, Copy, Replace, Skip, capacity, verify, report | Covered |
| `shaderpacks` | Directory | On | Include/Exclude, Copy, Replace, Skip, capacity, verify, report | Covered |
| `schematics` | Directory | On | Include/Exclude, Copy, Replace, Skip, capacity, verify, report | Covered |
| `saves` | Directory | Off | Explicit opt-in plus Copy/Replace/Skip/capacity/verify/report | Covered with safer behavior |
| `screenshots` | Directory | Off | Explicit opt-in plus Copy/Replace/Skip/capacity/verify/report | Covered with safer behavior |
| `XaeroWaypoints` | Directory | On | Include/Exclude, Copy, Replace, Skip, capacity, verify, report | Covered |
| `XaeroWorldMap` | Directory | On | Include/Exclude, Copy, Replace, Skip, capacity, verify, report | Covered |
| `itemscroller` | Directory | On | Include/Exclude, Copy, Replace, Skip, capacity, verify, report | Covered |
| `g4mespeed` | Directory | On | Include/Exclude, Copy, Replace, Skip, capacity, verify, report | Covered |

## Feature parity matrix

| Original behavior | Reference location | New implementation | Automated test | Status |
| --- | --- | --- | --- | --- |
| Choose source/destination folders | `Form1.cs` 176-191 | WPF folder pickers and typed root selection | `MigrationPreviewViewModelTests` | Covered |
| Eleven checkboxes | `Form1.Designer.cs`; `Form1.cs` 83-130 | Catalog-backed bounded preview rows | `AllPresetContainsEveryKnownCandidateInCatalogOrder`; App preview tests | Covered |
| Select all | `Form1.cs` 194-207 | `MigrationSelectionPresets.All` and `SelectAllCommand` | `AllPresetContainsEveryKnownCandidateInCatalogOrder`; `SelectAllIncludesEveryKnownCandidateWithoutReinspection` | Covered |
| Select none | `Form1.cs` 209-222 | `SelectNoneCommand` with explicit exclusions | `SelectNoneExcludesEveryKnownCandidateWithoutReinspection` | Covered |
| Recommended action | `Form1.cs` 224-234 | Deterministic reset to nine entries; worlds/screenshots off | `RecommendedPresetMatchesLegacyDirectionAndKeepsWorldsAndScreenshotsOptIn`; `ResetRecommendedRestoresDefaultSelection` | Covered with safer behavior |
| `options.txt` copy | `Form1.cs` 79-86 | Reviewed create-only Copy or explicit Replace | `CopyFileIsCreateOnlyAndVerifierMatchesSource` | Covered with safer behavior |
| Recursive directory copy | `Form1.cs` 133-172 | Handle-relative no-follow traversal | `CopyDirectoryExcludesHanemodClientAtEveryDepthAndVerifierUsesSameRule`; reparse tests | Covered with safer behavior |
| `hanemod-client.json` exclusion | `Form1.cs` 152-164 | Domain-owned known rule used by every payload adapter | `KnownMigrationContentRulesTests`; Infrastructure integration tests | Covered with safer behavior |
| Overwrite | `Form1.cs` 163 | explicit Replace, verified backup, live revalidation, journal, verification, guarded rollback | execution/backup/rollback integration suites | Covered with safer behavior |
| Missing selected source throws | fixed calls at `Form1.cs` 91-130 | typed `SourceMissing` no-op or blocked stale evidence | planner/workflow tests | Covered with safer behavior |
| Final folder-name warning | `Form1.cs` 31-43 | absolute local path, overlap, canonical-handle, containment and reparse checks before writes | workspace safety and integration tests | Intentionally changed |
| No-selection warning | `Form1.cs` 44-46 | valid zero-write Preview/report, no mutation | selection, workflow, report tests | Covered with safer behavior |
| Completion dialog | `Form1.cs` 48-51 | verified Completed state and read-only report | App tests and FlaUI confirmed migration smoke | Covered with safer behavior |
| Raw exception message | `Form1.cs` 54-56 | typed and redacted user-visible failures | App privacy regression tests | Intentionally changed |
| Create missing directories | `Form1.cs` 62-67, 135-140 | create-only handle-relative writes inside validated roots | execution integration tests | Covered with safer behavior |
| Copy directory attributes | `Form1.cs` 69-70, 142-143 | payload bytes and structure only; exact NTFS clone remains outside the contract | architecture docs and verification tests | Intentionally changed |
| Synchronous UI execution | `Form1.cs` 22-57 | asynchronous commands, busy state and cancellation | App workflow tests | Covered with safer behavior |
| User-visible exclusion reason | console only at `Form1.cs` 156-158 | Preview and Report expose the known rule summary | preview/report projector/ViewModel tests | Covered with safer behavior |

There are no `Missing` rows.

## `hanemod-client.json` rule

The rule matches the **basename of a file** at every depth inside any selected directory candidate.
It is not a path, glob, regular expression, or user-defined pattern. A directory with that name is not
silently skipped, and a similar file such as `hanemod-client.json.bak` remains normal migration content.

The reference uses case-sensitive `string ==`. The new rule uses `OrdinalIgnoreCase`, matching the rest
of the Windows path policy and preventing a case-only spelling from bypassing the exclusion. The same
rule controls Copy, Replace preservation, Backup, rollback restore, independent fingerprints, capacity
measurement, Preview summary, and Report summary.

During Replace, an excluded file already present at the destination is preserved in place. It is not
copied from the source, placed in the backup artifact, deleted by replacement, or restored from backup.
All non-excluded content continues to use reviewed Replace semantics.

## Intentional safer differences

- unconditional overwrite becomes explicit Skip or Replace;
- Replace requires a validated backup, live revalidation, durable journal, independent verification,
  and fingerprint-guarded rollback;
- simple path recursion becomes handle-relative no-follow traversal;
- path-name checks after copy become path, containment, overlap, canonical-path and reparse gates before IO;
- missing inputs become typed evidence instead of uncontrolled exceptions;
- raw exception text becomes bounded, path-free UI state;
- execution is asynchronous and cancellable at defined boundaries;
- capacity preflight must be current and Ready before execution;
- Recommended is a deterministic reset, so prior world/screenshot choices cannot remain selected by accident.

## Unsupported by design

These items are separate from ModPackTransfer feature parity:

- automatic rollback resume;
- Merge semantics;
- Minecraft/mod/loader compatibility analysis;
- exact NTFS metadata cloning;
- report persistence/export;
- release packaging, signing, and installer work.

Copy success does not prove that migrated configuration is compatible with another Minecraft, loader,
mod, or mod-pack version.
