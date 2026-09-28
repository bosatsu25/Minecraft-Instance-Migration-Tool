# Phase 5.0 Release Hardening implementation plan

The approved Phase 5.0 brief turns the existing Windows application into a reproducible release candidate without publishing v1.0.0.

## Decisions

- Development version: `0.9.0`, because clean-machine release validation still belongs to Phase 5.1.
- Target: Windows 11 x64, `win-x64`, self-contained folder publish.
- Packaging: portable ZIP plus a per-user Inno Setup installer.
- Single-file, trimming, ReadyToRun, and Native AOT stay disabled for predictable WPF and native interop behavior.
- Pull requests and manual runs produce explicitly unsigned artifacts. Production signing is allowed only for trusted version tags and must fail closed when signing secrets are unavailable.
- The missing original application icon and repository license remain explicit release blockers. No substitute artwork or license grant will be invented.

## Tasks

1. Add failing App tests for assembly-derived product/version information and release configuration invariants.
2. Establish central version and product metadata, an x64 self-contained publish profile, and a small About surface.
3. Add controlled release scripts for publish, launch smoke, ZIP, installer, signing verification, checksums, and package-content validation.
4. Add a parameterized per-user Inno Setup definition with stable upgrade identity and user-data-preserving uninstall behavior.
5. Add a least-privilege release workflow for PR/manual dry-runs and trusted tag signing/release preparation.
6. Document installation, versioning, signing, support, known blockers, and the Phase 5.1 handoff.
7. Run core tests, FlaUI, formatting, local release packaging, artifact inspection, and an independent diff audit.
8. Commit, push, open the Phase 5.0 PR, and require hosted `verify`, `ui-smoke`, and release dry-run/package success before declaring completion.
