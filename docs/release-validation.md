# Member distribution validation

The owner has chosen a free **unsigned community edition** for Windows 11 x64. This supersedes
the former rule that production signing was a prerequisite for every release. It does not change
the migration safety contracts or turn an untested item into PASS. Optional paid signing remains
unvalidated and outside the member-edition acceptance criteria.

## DONE for this scope

The distributable ZIP and installer must be built from the current source, include the runtime
and a Japanese member guide, pass content/checksum/metadata checks, start without a separate .NET
installation, complete real Copy and Replace migration with verification, and pass install,
reinstall, shortcuts, uninstall, and preservation of unrelated data. Core safety and compatibility
regressions must remain green. Publishing a GitHub Release is a separate human-gated operation.

## Current-source evidence

The table below records the earlier local-only member packaging pass. The subsequent bilingual/theme
release adds pre-backup workspace validation and canonical drive-alias protection. Its acceptance
requires fresh packaging and hosted checks; the old hashes below do not identify the updated binaries.

Local bilingual implementation evidence: Release build has zero warnings/errors; Domain 110,
Application 136, Infrastructure 131, and App 81 tests passed (458 total). Japanese default, English
switching, all three themes, preserved paths/selection, confirmed migration and report were exercised
through FlaUI. A 256 MiB file migration passed the bounded-memory regression. Theme switching 100 times
kept one palette. A fixed phrase index replaces timing-sensitive regular-expression translation.

All ten UI tests also passed against the newly generated ZIP application with external .NET lookup
disabled. The measured 256 MiB case used 104 MiB private memory before migration, 145 MiB after, and
180 MiB peak working set on this host; these measurements are not universal limits. Dependency audit
reported no known vulnerable packages from the configured NuGet source. The bilingual installer
compiled and package content/checksum/startup verification passed. Local install/uninstall validation
refused existing application registration and preserved it; the new hosted Windows runner supplies
the clean install/reinstall/uninstall gate before artifact upload and draft-release creation.
Hosted evidence belongs to the tag's Actions run; this pre-tag local record does not claim hosted PASS.

Security audit: 209 text files reviewed; two binary icon assets excluded. A low-severity backup
drive-alias overlap finding was reproduced, fixed, and regression-tested. Required backup now also
validates workspace safety before any backup write; cancelled, invalid and exceptional checks refuse
the operation. Independent safety and UI diff reviews identified no remaining product blocker within
their reviewed scope. Optional signed-mode credential inheritance remains a separately reviewed
hardening area; the chosen unsigned release does not populate signing credentials. No claim of
universal safety or full fresh-machine validation is made.

| Item | Status | Evidence |
| --- | --- | --- |
| Release-mode routing without credentials | PASS | Test-ReleaseMode.ps1: default unsigned tag, explicit unsigned/signed tags, branch/PR isolation, invalid configuration, and tag/version mismatch. |
| Release build, warnings, all deterministic tests, formatting | PASS | SDK 10.0.401: restore/build succeeded, 0 warnings/errors, Domain 110 + Application 132 + Infrastructure 130 + App 68 = 440 passed; format verification and git diff --check passed. |
| Reference candidate behaviour | PASS | User-supplied ModPackTransfer source ZIP inspected in place; eleven candidates, Recommended, All/None, and recursive exclusion match the existing compatibility matrix. Reference source is not redistributed. |
| Self-contained ZIP and per-user installer generation | PASS | Existing Build-Release.ps1 generated fresh version 1.0.0 packages from the current working tree. |
| Packaged runtime, licenses, Japanese member guide, absence of development/private files | PASS | Verify-Release.ps1 checked runtime, licenses, START-HERE.ja.txt, development/private file patterns, repository-path leakage, and ZIP/publish manifests. |
| Final checksum and version match | PASS | EXE/installer metadata matches 1.0.0; final ZIP and installer SHA-256 match SHA256SUMS.txt; both Authenticode states are NotSigned. |
| Packaged Copy, Replace, Backup, verification, Report | PASS | Eight FlaUI tests passed against the final extracted ZIP EXE, including confirmed Copy and Replace, original bytes retained in backup, and successful Report verification. |
| Packaged eleven-candidate All selection and exclusion preservation | PASS | Actual ZIP EXE passed the new All fixture: all eleven items, opt-in saves/screenshots, nested source exclusion, retained destination exclusion, unrelated data, and verified replacement backup. |
| Install, direct/Start Menu/desktop launch, reinstall, uninstall, external data preservation | PASS | Test-Installer.ps1 completed default-path per-user install, all three launch surfaces, same-version reinstall, uninstall, and sentinel preservation. Japanese guide presence was also checked. |
| Startup with external .NET lookup disabled | PASS | Validate-CleanMachine.ps1 and all packaged UI processes used nonexistent DOTNET_ROOT/DOTNET_ROOT_X64, multilevel lookup off, and system-only PATH. No separately installed runtime was used by the self-contained app. |
| Production Authenticode signing | NOT APPLICABLE | The selected edition is explicitly unsigned; no paid certificate, subscription, or protected signing-environment approval is required. |
| Signed-mode failure and artifact labeling | PASS | Explicit signed mode never falls back after missing credentials; signing mode selects both upload and draft-release download labels. YAML parsing and PowerShell syntax checks also passed; Test-ReleaseMode.ps1 executed nine routing/rejection cases. |
| Interactive SmartScreen / organisation policy | NOT TESTED | Not reproducible as a universal CI behaviour. Unsigned warning and cancellation guidance are documented; no promise of warning-free startup. |
| Fresh physical Windows 11 machine / VM | NOT TESTED | This session has no dedicated fresh machine. Developer-machine results are not mislabeled clean-machine results. |
| Visual small-icon rendering on every shell surface | NOT TESTED | Structural icon and wiring are covered by existing automated tests; exhaustive visual review is not claimed. |
| Current-change hosted Actions | NOT TESTED | Local changes have not been pushed; previous runs are not evidence for this patch. |

NOT TESTED items for optional signing, reputation, extra platforms, and cosmetic shell validation
are not member-edition functional blockers. Failed migration, corruption, unresolved core regression,
missing runtime, checksum mismatch, or broken install/uninstall remain blockers.

## Historical evidence and exclusions

The earlier implementation at `ac27501d863c2d82c4d0d86776f041aac88f0bba` passed 440 deterministic
tests, seven UI smoke tests, and hosted Windows verification. The current patch must take fresh local
evidence before delivery (now recorded above). Historical hosted success is not a claim of current-patch hosted success.

Windows 10, automatic launcher detection/integration, mod/version compatibility analysis, Merge,
automatic recovery resume, and report export are outside this edition. Recovery is guarded and
explicitly confirmed; it is not an atomic filesystem transaction or an automatic retry.

## Earlier local-only decision (superseded by release work)

- **Member package ready: Yes — locally validated unsigned Windows 11 x64 edition**
- **Public GitHub Release published by this work: No**

Validation date: 2026-10-09 (Asia/Tokyo). Actual local host: Windows 11 Home x64
`10.0.26300`. This is a developer machine, not a fresh VM. No new commit, push, tag, or public
release was performed during this member-distribution fix. Base commit: `9436f5c`.

Final package identities:

| File | Bytes | SHA-256 |
| --- | --- | --- |
| MinecraftInstanceMigrationTool-1.0.0-win-x64.zip | 65,741,597 | `008bdf39ab3564a16b0933f272997d581f66811484e8a92edff6945ee5372158` |
| MinecraftInstanceMigrationTool-1.0.0-win-x64-setup.exe | 46,954,186 | `f1c7a1a0f301556534bd279fe7ff0a8a67bb96daaa6e5233257abfc765349e6a` |

The independent diff review found and fixed signed-label leakage, the unwanted paid-environment
gate, silent signed-mode fallback, stale tag version acceptance, misleading support/atomic-recovery
claims in release notes, and missing member instructions. Migration policy and storage engines were
not changed. A new test initially invoked Apply after Select all; the existing Select all command
already applies immediately. The fixture and guide were corrected to that actual behaviour.
