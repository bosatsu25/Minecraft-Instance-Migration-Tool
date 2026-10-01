# Release process

Phase 5.0 prepares the Windows release pipeline. Phase 5.1 validates an untagged `1.0.0`
release-candidate build; it does not publish `v1.0.0`.

## Fixed release configuration

| Setting | Value |
| --- | --- |
| Release-candidate version | `1.0.0` from `Directory.Build.props` |
| Supported release target | Windows 11 x64 |
| Runtime identifier | `win-x64` |
| Deployment | self-contained folder publish |
| Single-file | disabled |
| Trimming / Native AOT / ReadyToRun | disabled |
| Installer | Inno Setup 6.7.3, per-user |
| Package names | `MinecraftInstanceMigrationTool-{version}-win-x64.zip`, `MinecraftInstanceMigrationTool-{version}-win-x64-setup.exe`, `SHA256SUMS.txt` |

The folder publish was selected because WPF, native Windows filesystem calls, and recovery behavior benefit from the least transformed output. Single-file adds a large bundle and another loading/extraction mode without improving the installer experience. Trimming and AOT are deferred until a separate compatibility effort can prove the full migration and recovery paths.

The Phase 5.0 comparison used the same SDK, RID, self-contained mode, and disabled trimming/AOT. Both forms opened the WPF main window. The normal publish contained 403 files / 146,725,125 bytes with a 162,816-byte app host; the single-file evaluation contained one 140,106,806-byte executable. A roughly 4.5% directory-size reduction does not offset native-library extraction behavior, less transparent diagnostics, or the lack of benefit once an installer/ZIP is used, so the normal folder publish is the release format.

## Local dry-run

Install the official Inno Setup 6.7.3 compiler, verify its Authenticode signature, then run:

```powershell
./eng/release/Build-Release.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
./eng/release/Test-Installer.ps1 `
  -InstallerPath ./artifacts/release/MinecraftInstanceMigrationTool-1.0.0-win-x64-setup.exe `
  -ExpectedVersion 1.0.0 `
  -TestReinstall `
  -CreateDesktopShortcut
```

The build restores the `win-x64` runtime, publishes without PDBs, launches the published application, creates both packages, verifies package contents and version metadata, and writes SHA-256 checksums. Output is restricted to ignored `artifacts/` directories.
Before clearing output, the build rejects reparse points in the repository-to-output path. The current packaging script accepts numeric `major.minor.patch` versions only; prerelease labels require a separate numeric Windows file-version policy before use.

The workflow downloads the immutable Inno Setup 6.7.3 asset and checks its SHA-256 against the digest published on the [official release](https://github.com/jrsoftware/issrc/releases/tag/is-6_7_3) (`9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732`). It also requires a valid Authenticode signature from Pyrsys B.V. before running the compiler installer.

## CI modes

`.github/workflows/release.yml` supports:

- pull-request, Phase 5.0/5.1 branches, and manual unsigned dry-runs after core and UI verification;
- trusted `v*` tags, which require valid signing configuration before a draft GitHub release can be created.

Normal jobs have `contents: read`. Only the tag-only draft-release job receives `contents: write`. Pull requests never receive or use production signing material.

## Signing boundary

Unsigned dry-run artifacts are intentionally reported as unsigned. Trusted tag builds require:

- secret `WINDOWS_SIGNING_CERTIFICATE_BASE64`;
- secret `WINDOWS_SIGNING_CERTIFICATE_PASSWORD`;
- variable `WINDOWS_SIGNING_TIMESTAMP_URL`.

Configure these values in the repository's Actions secrets/variables before creating a production
version tag. The Base64 secret must contain the production code-signing PFX, the password secret
must unlock it, and the variable must be a usable RFC 3161 timestamp URL. Do not put either secret
in source, workflow output, or release artifacts.

Only a `v*` tag run receives signing material. It signs the application executable before ZIP and
installer creation, signs the installer afterwards, verifies both signatures with
`Get-AuthenticodeSignature`, and generates checksums only after signing succeeds. SignTool uses
SHA-256 for both the file and timestamp digests (`/fd SHA256 /td SHA256`) and requires the
timestamp URL (`/tr`). Missing configuration, a failed signing command, or invalid signature fails
the run closed. The temporary PFX is written under the runner temporary directory and a tag-only
`always()` cleanup step removes it; cleanup failures fail the workflow instead of being suppressed.
After both signatures are verified, the build also removes the PFX and clears the password
environment variable before launching the signed application or running package/installer smoke
tests. The workflow cleanup remains as a failure-path fallback.

Unsigned artifacts are uploaded as `MinecraftInstanceMigrationTool-unsigned-dry-run-{run_id}`.
Signed tag artifacts use the separate `MinecraftInstanceMigrationTool-production-signed-{run_id}`
name, and the draft release consumes only that signed artifact. This prevents unsigned validation
packages from being mistaken for production release assets.

The repository-side path is ready for these settings, but production signing is not considered
validated until the real production certificate and timestamp service are configured and a trusted
tag run verifies the resulting signatures. No certificate or credential is stored in the repository.

The equivalent signing command is `signtool sign /fd SHA256 /td SHA256 /tr <timestamp-url> /f <certificate.pfx> /p <password> <artifact>`. The password is supplied only through the Actions secret environment and is never committed or written to a tracked file.

## Versioning

Semantic Versioning is used. `0.x.y` is development, `1.0.0` is the first stable release, patch releases contain compatible fixes, minor releases contain compatible features, and major releases may change behavior incompatibly. Assembly, file, product, About UI, package, and installer versions derive from the central props file.

## Current release blockers

- Production Authenticode credentials are not configured.

This blocker prevents a stable public release. The unsigned CI artifact is for verification only.
The approved original icon is stored as `AppIcon.png` and a multi-resolution `AppIcon.ico` under the App's `Assets/` directory. The executable, window, installer, shortcuts, and uninstall entry use that icon; the source PNG is excluded from publish output.
The repository uses the MIT license in `LICENSE`; it is included in both release packages.
Phase 5.1 evidence and blockers are recorded in [release validation](release-validation.md).

## Phase 5.1 validation

Validate clean-machine install, first launch, Copy, Replace, capacity failure, recovery/rollback, report, upgrade, uninstall, signature, and checksums before creating a stable tag or public release.
