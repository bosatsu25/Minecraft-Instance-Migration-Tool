# Minecraft Instance Migration Tool

[日本語](README.ja.md)

A planned Windows desktop tool for selectively moving Minecraft user data from an old
instance to a new one when changing mod packs or launch configurations.

**Status: Phase 2.1 planning policy.** The app can inspect one local folder in the UI, and the
Domain/Application layers can build a read-only migration plan from source/destination observations.
A Recommended selection preset and explicit destination-conflict decisions (Skip / Replace) now exist.
The tool still does not copy, back up, or modify files.

## Why

User settings, worlds, and other personal data deserve a reviewable migration plan,
explicit backup, and verification. The intended flow is:

Inspect → Plan → Preview / Dry Run → Backup → Execute → Verify → Report

On verification failure: Diagnose → Rollback → Report. Planner semantics now exist in the core;
Preview / Dry Run and every write-capable stage remain future work.

## Architecture

- **Domain:** immutable observation models, deterministic plan policy, Recommended selection defaults, and explicit conflict intent; compatibility/nested exclusion rules remain future work.
- **Application:** use cases and ports, referencing Domain.
- **Infrastructure:** Windows metadata adapter, referencing Application/Domain.
- **App:** WPF view, MVVM presentation state, and composition root.
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
5. Separate later changes: nested exclusions / compatibility → Preview → Backup → Executor → Verifier → Report,
   with diagnosis and rollback failure paths.

Each node gets a focused issue/PR and its own acceptance tests before the next expansion.
Legacy selection candidates are documented in [migration rules](docs/migration-rules.md).
Phase 2.1 implements the legacy Recommended selection direction: all known candidates are selected
except `saves` and `screenshots`, which remain opt-in. Destination conflicts remain unresolved
unless the caller explicitly chooses Skip or Replace. The legacy `hanemod-client.json` exclusion,
Merge semantics, compatibility claims, and write execution are not implemented.
