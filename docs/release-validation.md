# Phase 5.1 release validation

This checklist records evidence for the untagged `1.0.0` release-candidate build. The packaging
scripts intentionally accept numeric Windows versions only, so Phase 5.1 validates the exact
`1.0.0` metadata without creating a tag or public release. A PASS applies only to the candidate
identified by the pull-request head and its corresponding GitHub Actions run.

Status meanings are strict: PASS has deterministic evidence, FAIL is a confirmed blocker,
NOT TESTED has no adequate evidence, and NOT APPLICABLE does not apply to this release design.

## Candidate identity

| Item | Status | Evidence |
| --- | --- | --- |
| Source baseline contains final icon merge `3f25f652ce1de312ba395a11ac590930164e2054` | PASS | Phase 5.1 branch starts at that `main` commit. |
| Candidate version is consistent | PASS | Local EXE, installer, package names, installed registration, and About source metadata resolve to `1.0.0`. Hosted confirmation remains below. |
| Candidate commit is the hosted run head | NOT TESTED | Pending pull-request run. |

## Portable ZIP

| Item | Status | Evidence |
| --- | --- | --- |
| ZIP builds from the supported `win-x64` self-contained profile | PASS | Local formal pipeline produced 404 files / 147,175,217 bytes before compression. |
| ZIP extracts into a new owned temporary directory | PASS | Local verification extracted and removed a unique temporary directory. |
| Extracted application opens a WPF window | PASS | Local package verifier and FlaUI opened the extracted EXE. |
| Required runtime, license, and notices are present | PASS | `coreclr.dll`, `hostfxr.dll`, `hostpolicy.dll`, `LICENSE`, and `THIRD-PARTY-NOTICES.txt` verified. |
| Source, tests, debug files, raw icon assets, and developer repository paths are absent | PASS | Artifact scan includes source/project/debug/credential/log patterns and binary UTF-8/UTF-16 repository-path scanning. |
| ZIP file list exactly matches the verified publish directory | PASS | Sorted publish and ZIP manifests matched. |

## Installer and uninstall

| Item | Status | Evidence |
| --- | --- | --- |
| Per-user install and HKCU uninstall registration | PASS | Local isolated install used lowest privileges and created no HKLM registration. Hosted confirmation remains below. |
| Default install path | PASS | Candidate installed and launched from `%LOCALAPPDATA%\Programs\Minecraft Instance Migration Tool`, then uninstalled cleanly. |
| Direct executable and Start Menu launches | PASS | Both opened the installed WPF window locally. |
| Desktop shortcut remains opt-in and launches when selected | PASS | Absent on first install; created, validated, and launched when selected on reinstall. |
| Same-version reinstall remains usable and does not duplicate the Start Menu shortcut | PASS | Local reinstall retained one shortcut and a usable registration. |
| Display name, publisher, version, install location, and icons are consistent | PASS | Installed binary and HKCU registration were checked against `1.0.0` and `bosatsuKing`; shortcut icon targets matched the app EXE. |
| Uninstall removes owned binaries, shortcuts, and registration | PASS | Local silent uninstall removed all installer-owned surfaces. |
| Uninstall preserves user-owned data outside the install directory | PASS | A sentinel outside `{app}` remained byte-identical after uninstall. |
| Previous-version upgrade | PASS | Rebuilt Phase 5.0 commit `0c47788` as `0.9.0`, installed it, launched it, upgraded in place to `1.0.0`, reinstalled, and uninstalled successfully. |

## Product and safety smoke

| Item | Status | Evidence |
| --- | --- | --- |
| Packaged application completes Inspect → Select → Preview → Capacity → Execute → Verify → Report for owned Copy fixture | PASS | Extracted ZIP EXE completed the FlaUI Copy migration and verified copied bytes and report. |
| Packaged application Replace flow | PASS | Extracted ZIP EXE selected Replace, created backup evidence, executed, verified output, retained original bytes in backup, and displayed the report. |
| Source/destination overlap, reparse, unresolved conflict, insufficient capacity, and stale evidence fail closed | PASS | Candidate deterministic suite: 437 tests passed locally. |
| Copy verification, Replace backup requirement, report projection, and private-data redaction remain intact | PASS | Candidate deterministic suite plus packaged Copy/Replace FlaUI smoke passed locally. |
| ModPackTransfer candidate, preset, Skip/Replace, and recursive exclusion compatibility remains intact | PASS | Candidate deterministic compatibility regressions passed locally. |

## Artifact, metadata, and legal audit

| Item | Status | Evidence |
| --- | --- | --- |
| ZIP and installer SHA-256 values and filenames match `SHA256SUMS.txt` | PASS | Local verification recomputed both SHA-256 values and required exactly two checksum records. |
| Checksums are generated after the final binary mutation | PASS | Pipeline signs first when required, then writes checksums, then reopens and verifies packages. |
| EXE, installer, package filenames, About, and installed metadata agree on `1.0.0` | PASS | Local package and install metadata audit passed. |
| Production package excludes xUnit, FlaUI, Microsoft.NET.Test.Sdk, and testhost | PASS | Local publish/ZIP audit found none of these test dependencies. |
| Multi-resolution 16/24/32/48/64/128/256 icon resource remains wired to app and installer | PASS | Automated App configuration test passed and release packaging uses the same ICO. |
| Small-icon rendering has no halo or damaged transparency on Windows shell surfaces | NOT TESTED | Requires visual Windows shell inspection. |
| MIT `LICENSE`, `THIRD-PARTY-NOTICES.txt`, and unofficial-product disclaimer are present and consistent | PASS | Repository source and package policy inspected; package inclusion still has a separate pending check above. |

## Environment and trust

| Item | Status | Evidence |
| --- | --- | --- |
| Clean GitHub-hosted Windows runner | NOT TESTED | Pending candidate run. This does not claim full fresh-user-machine equivalence. |
| Fresh Windows 11 VM or physical clean machine | NOT TESTED | No dedicated VM or physical clean machine is available in this phase environment. |
| Production Authenticode signing | FAIL | Production certificate and timestamp configuration are not available. Self-signing is not accepted as evidence. |
| SmartScreen and Unknown Publisher UX | NOT TESTED | SmartScreen reputation is not reproducible in CI and needs clean interactive Windows validation. |

## Hosted gates

| Item | Status | Evidence |
| --- | --- | --- |
| CI `verify` | NOT TESTED | Pending pull-request head run. |
| CI `ui-smoke` | NOT TESTED | Pending pull-request head run. |
| Release dry-run `verify` | NOT TESTED | Pending pull-request head run. |
| Release dry-run `ui-smoke` | NOT TESTED | Pending pull-request head run. |
| Release dry-run `release-package` | NOT TESTED | Pending pull-request head run. |

## Current decision

- **Phase 5.1 Complete: No**
- **v1.0 Release Ready: No**

Production signing is a confirmed project-policy blocker. Fresh-machine visual shell/SmartScreen
validation and all pending candidate checks must also be resolved before a
stable tag or public release. No PASS may be inferred from an older commit's run.

The local environment was Windows 11 x64 build `10.0.26300` with .NET SDK `10.0.401`; it was a
developer machine and is not treated as clean-machine evidence. One first full packaged-suite run
entered the product's safe `RecoveryRequired` state for Copy and Replace. The failure evidence had
already been removed by fixture cleanup, and the issue did not reproduce in the next isolated Copy run
or the next complete seven-test packaged run. Hosted clean-runner evidence is required before changing
that observation's release assessment.
