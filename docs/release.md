# Build and distribute the member edition

The default distribution is a free, **unsigned community build** for Windows 11 x64.
Members need neither a developer SDK nor a signing account: the ZIP and per-user installer
include the .NET runtime. A missing production-signing subscription is **not a blocker** for this
edition. Unsigned does not mean signed, trusted by SmartScreen, or immune to organisation policies.

## Completion criteria

**v1.0.0: DONE.** The unsigned member edition has been implemented, validated, and
[publicly released](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/releases/tag/v1.0.0).
Phase 5.0 and Phase 5.1 are closed for this edition; no required release task remains.

Member distribution is ready when the current source builds without warnings, safety tests pass,
and the actual ZIP/installed EXE completes owned-fixture migration with independent verification.
Packaging must include runtime files, licenses, the member guide, and matching checksums; exclude
source/test/private files; and pass install, launch, reinstall, and uninstall checks.
Paid signing, launcher integration, Windows 10 support, Merge, automatic resume, and report export
are outside this release scope and are not pending tasks for the stated Windows 11 member workflow.
See [release validation](release-validation.md) for actual results and their limits.

## One existing packaging pipeline

The central numeric version is `1.0.0` in `Directory.Build.props`. The supported profile is an
untrimmed, non-single-file, self-contained `win-x64` folder. Use the configured .NET SDK and
verified Inno Setup 6.7.3 compiler on a developer/build machine:

```powershell
./eng/release/Build-Release.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
./eng/release/Test-Installer.ps1 `
  -InstallerPath ./artifacts/release/MinecraftInstanceMigrationTool-1.0.0-win-x64-setup.exe `
  -ExpectedVersion 1.0.0 -TestReinstall -CreateDesktopShortcut -UseDefaultInstallPath
./eng/release/Validate-CleanMachine.ps1 -ArtifactsRoot ./artifacts -ExpectedVersion 1.0.0
```

Share the ZIP or installer with `SHA256SUMS.txt` from `artifacts/release/`. Both packages include
`START-HERE.ja.txt`, `LICENSE`, and `THIRD-PARTY-NOTICES.txt`. Members open the complete extracted
ZIP's `MinecraftInstanceMigrationTool.exe`, or install and use the Start Menu shortcut. They do
not run the build scripts. [Installation](install.md) explains the trust boundary and user flow.

Output cleanup stays inside the ignored repository `artifacts/` directory and rejects reparse
ancestors. Checksums are generated only after the final binary mutation and independently checked.
Inno Setup's compiler download is pinned to 6.7.3 and verified by SHA-256 and Authenticode in CI.

## Explicit CI release mode

The optional repository variable `MIM_RELEASE_SIGNING_MODE` accepts `unsigned` or `signed`.
Absent/empty means `unsigned`; invalid values fail. Branch/PR builds always run unsigned and
never receive signing credentials, regardless of this setting. Every version tag must exactly
match the central version, including unsigned tags.

| Build | Environment | Artifact label |
| --- | --- | --- |
| PR or branch | `release-dry-run` | `unsigned-dry-run` |
| Version tag, default/unsigned mode | `release-dry-run` | `unsigned-community` |
| Version tag, explicitly signed mode | `production-signing` | `production-signed` |

`release-package` depends on core/UI verification and the release-mode gate. The draft-release
job downloads the same accurately labeled artifact and adds its actual signing mode to the notes.
A version tag creates a **draft** only. Publishing, merging, and creating tags remain human gates.
The v1.0.0 publication gate was authorized and completed; future tags/publications still require authorization.
README-only changes remain excluded from CI.

## Optional signed edition

The existing Cloud HSM path (DigiCert KeyLocker) remains available only if the owner deliberately
chooses `signed`. Its private key stays outside the repository. Configure the protected
`production-signing` environment, its reviewers/tag policy, and:

- secrets `SM_API_KEY`, `SM_CLIENT_CERT_FILE_B64`, `SM_CLIENT_CERT_PASSWORD`;
- variables `SM_HOST`, `MIM_PRODUCTION_SIGNING_CERT_THUMBPRINT`, `MIM_PRODUCTION_SIGNING_TIMESTAMP_URL`
  (the thumbprint may also be an environment secret).

Missing signed-mode configuration or signature failure aborts that mode; it never silently
downgrades to unsigned. The app and installer are verified before final checksums. Workflow cleanup
removes temporary client authentication material after the build; isolation of cloud-signing credentials
from build-time application smoke launches has not been validated by the unsigned release.
Self-signing is not accepted as evidence. This optional mode has not been production validated and
is outside the completed member-edition scope.
