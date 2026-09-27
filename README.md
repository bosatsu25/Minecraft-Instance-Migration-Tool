# Minecraft Instance Migration Tool

[日本語](README.ja.md)

A planned Windows desktop tool for selectively moving Minecraft user data from an old
instance to a new one when changing mod packs or launch configurations.

**Status: Phase 4.0 Migration Workflow / Session.** The app UI remains Inspector / Preview only.
Guarded automatic rollback creates a separate append-only, checksummed rollback-attempt journal before
any destructive rollback action. Each action must durably record Started before storage mutation and then
Applied / GuardRejected / Failed afterward. Execute / rollback UI controls and reporting are still absent.

## Why

User settings, worlds, and other personal data deserve a reviewable migration plan,
explicit backup, and verification. The intended flow is:

Inspect → Plan → Preview / Dry Run → Backup → Execute → Verify → Report

On verification failure: Diagnose → Rollback → Report. The backend now contains verified backup,
durable execution evidence, production Copy / Replace, independent verification, and guarded rollback IO.
User-facing Execute / Rollback controls and reporting remain future work.

## Architecture

- **Domain:** immutable observation models, deterministic plan policy, Recommended selection defaults, and explicit conflict intent; compatibility/nested exclusion rules remain future work.
- **Application:** use cases and ports, referencing Domain.
- **Infrastructure:** Windows metadata and handle-relative backup adapters, referencing Application/Domain.
- **App:** WPF Inspector and Preview / Dry Run views, MVVM presentation state, and composition root.
- **Tests:** xUnit behavior/integration/ViewModel tests and architecture regression checks.

App references Application and Infrastructure for composition; Infrastructure never flows
back into Application or Domain. See [architecture](docs/architecture.md).

## Development

Use Windows with the .NET 10 SDK selected by [global.json](global.json) (10.0.401,
latest patch in that feature band). The SDK includes the WPF build tools. An IDE is optional.

From the repository root:

```powershell
dotnet restore
dotnet build --configuration Release --no-restore
dotnet run --project src/MinecraftInstanceMigration.App --configuration Release --no-build
```

Dependency restore requires access to NuGet. The app itself has no network functionality.
The libraries target `net10.0`; the WPF host targets `net10.0-windows`.
Do not change target frameworks to bypass a missing Windows/SDK environment.

## Using the Inspector

Choose **Browse**, select a local folder (any name is accepted), then **Inspect**.
An absolute local drive path can also be entered. **Cancel** stops between metadata calls.
Changing the input clears previous observations. Inspect checks only:
`options.txt`, `config`, `resourcepacks`, `shaderpacks`, `schematics`, `saves`,
`screenshots`, `XaeroWaypoints`, `XaeroWorldMap`, `itemscroller`, and `g4mespeed`.

The table separates expected kind from actual state. Missing is distinct from inaccessible
or unavailable. Links are reported without following them; a link anywhere in the root path
blocks inspection. No recursion or content reading occurs. Known names do not establish a
Minecraft instance, compatibility, or migration eligibility. Results are not an atomic snapshot.
UNC, mapped network drives, device paths, relative paths, and parent traversal are unsupported.

## Verification

```powershell
dotnet test --configuration Release --no-build --no-restore
dotnet format --verify-no-changes --no-restore
git diff --check
git status --short --branch
```

Run restore and build first. Windows [CI](.github/workflows/ci.yml) runs restore, Release
build, tests, and formatting checks on pushes and pull requests. Warnings fail the build.
Use `dotnet format --no-restore` to apply C# formatting locally. The formatter does not
fully validate Markdown, YAML, or XAML layout; review those diffs too.

The small WPF UI smoke suite runs separately on Windows:

```powershell
dotnet test tests/MinecraftInstanceMigration.UiTests/MinecraftInstanceMigration.UiTests.csproj --configuration Release
```

See [testing](docs/testing.md) for its scope and a manual smoke procedure.

Development follows the [evidence-driven Graph Loop](docs/graph-loop.md).
See [AGENTS.md](AGENTS.md) for concise rules and [testing](docs/testing.md) for verification scope.

## Roadmap

1. Phase 0: solution, boundaries, tests, CI, documentation, minimal shell.
2. Phase 1: read-only Instance Inspector.
3. Phase 2: deterministic read-only Migration Planner core.
4. Phase 2.1: Recommended selection preset and explicit Skip / Replace conflict intent.
5. Phase 2.2: Preview / Dry Run model and WPF preview flow.
6. Phase 3.0: Backup Preflight and versioned manifest draft, with no filesystem writes.
7. Phase 3.1: Windows Backup IO foundation with owned roots, handle-relative traversal, cancellation,
   nested reparse rejection, and post-copy fingerprint verification.
8. Phase 3.2: read-only completed-backup revalidation against ownership marker, manifest, plan, and
   recomputed tree fingerprint.
9. Phase 3.3: versioned execution-journal contract and deterministic rollback planning, with no migration writes.
10. Phase 3.4: durable Windows execution-journal storage with append/flush ordering and crash-tail recovery.
11. Phase 3.5: live revalidation + execution orchestration contract, with mutation/verifier ports only.
12. Phase 3.6: Windows handle-relative Copy / Replace mutation plus independent post-write verification.
13. Phase 3.7: fingerprint-guarded DeleteCreatedEntry / RestoreFromBackup rollback IO.
14. Phase 3.8: durable rollback-attempt journal with crash-safe Started / terminal evidence.
15. Phase 4.0: Application-owned migration workflow/session connecting inspection, planning, preview, backup, and execution.
16. Separate later changes: selection/conflict UI → Backup/Execute UI → recovery/reporting UX → release hardening.

Each node gets a focused issue/PR and its own acceptance tests before the next expansion.
Legacy selection candidates are documented in [migration rules](docs/migration-rules.md).
Phase 2.1 implements the legacy Recommended selection direction: all known candidates are selected
except `saves` and `screenshots`, which remain opt-in. Destination conflicts remain unresolved
unless a caller explicitly chooses Skip or Replace. Phase 2.2 exposes a UI dry-run using the
Recommended preset; this UI currently displays unresolved conflicts rather than editing decisions.
The legacy `hanemod-client.json` exclusion, Merge semantics, size estimates, compatibility claims,
execution UI/reporting and automatic rollback resume are not implemented.
