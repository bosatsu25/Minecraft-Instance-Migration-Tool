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
| Candidate implementation commit is the hosted run head | PASS | Commit `c1caa6a0acfd27205813e65b11b8e85f2865ece0` is the head of the recorded CI and release dry-run. |

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
| Clean GitHub-hosted Windows runner | PASS | GitHub-hosted `windows-latest` completed package build, ZIP launch/migration smoke, default-path install/reinstall, and uninstall. This does not claim full fresh-user-machine equivalence. |
| Fresh Windows 11 VM or physical clean machine | NOT TESTED | No dedicated VM or physical clean machine is available in this phase environment. |
| Production Authenticode signing | FAIL | Production certificate and timestamp configuration are not available. Self-signing is not accepted as evidence. |
| SmartScreen and Unknown Publisher UX | NOT TESTED | SmartScreen reputation is not reproducible in CI and needs clean interactive Windows validation. |

## Hosted gates

| Item | Status | Evidence |
| --- | --- | --- |
| CI `verify` | PASS | [Run 36793799110 / verify](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/actions/runs/36793799110/job/110152491032). |
| CI `ui-smoke` | PASS | [Run 36793799110 / ui-smoke](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/actions/runs/36793799110/job/110152491252). |
| Release dry-run `verify` | PASS | [Run 36793798539 / verify](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/actions/runs/36793798539/job/110152489165). |
| Release dry-run `ui-smoke` | PASS | [Run 36793798539 / ui-smoke](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/actions/runs/36793798539/job/110152489430). |
| Release dry-run `release-package` | PASS | [Run 36793798539 / release-package](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/actions/runs/36793798539/job/110152865824). |

## Current decision

- **Phase 5.1 Complete: No**
- **v1.0 Release Ready: No**

Production signing is a confirmed project-policy blocker. Fresh-machine visual shell/SmartScreen
validation and all pending candidate checks must also be resolved before a
stable tag or public release. No PASS may be inferred from an older commit's run.

The local environment was Windows 11 x64 build `10.0.26300` with .NET SDK `10.0.401`; it was a
developer machine and is not treated as clean-machine evidence. One first full packaged-suite run
entered the product's safe `RecoveryRequired` state for Copy and Replace. The failure evidence had
already been removed by fixture cleanup, and the issue did not reproduce in the next isolated Copy run,
the next two complete seven-test packaged runs, or the hosted clean-runner package job. It remains an
independent-audit observation rather than an unexplained candidate failure.

## Wave 2 — Production signing readiness

The repository signing path is prepared for the production certificate and timestamp settings:
version-tag runs select a protected `production-signing` Actions environment, then verify the signed executable and installer before
checksums, upload to a production-signed artifact name, and remove the temporary runner-local PFX.
The candidate is not launched while the PFX or password environment variable is available. Unsigned
PR/branch dry-runs use a separately named artifact and cannot feed the draft-release job. This
repository-side readiness is not evidence that the environment protection rules or production
credentials have been configured, or that a production signature was produced.

| Item | Status | Evidence |
| --- | --- | --- |
| Repository signing workflow and fail-closed contract | PASS | Release configuration tests check tag gates, protected-environment selection, environment-only secret names, SHA-256 signing arguments, signing/checksum order, signature verification, PFX/password cleanup before candidate execution, cleanup fallback, and artifact separation. Hosted unsigned dry-run validates the non-production path only. |
| Production signing environment protection | NOT TESTED | GitHub environment reviewer and tag restrictions must be configured and independently confirmed in repository settings; legacy repository/organization signing secrets must be removed. |
| Production Authenticode signing | FAIL | Production certificate/password and timestamp configuration remain unavailable. No trusted signed-tag run was performed; self-signing is not accepted as evidence. |

Do not change the production-signing FAIL to PASS until the protected environment is configured,
legacy repository/organization signing secrets are removed, the real production certificate and
timestamp service are configured, and an approved trusted `v*` tag run verifies the published
signatures.
