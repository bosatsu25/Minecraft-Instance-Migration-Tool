# Minecraft Instance Migration Tool

[日本語](README.ja.md)

A planned Windows desktop tool for selectively moving Minecraft user data from an old
instance to a new one when changing mod packs or launch configurations.

**Status: Phase 0 foundation.** The application is a static WPF shell. It does not inspect,
copy, back up, or modify Minecraft files. It is not ready for real migrations.

## Why

User settings, worlds, and other personal data deserve a reviewable migration plan,
explicit backup, and verification. The intended flow is:

Inspect → Plan → Preview / Dry Run → Backup → Execute → Verify → Report

On verification failure: Diagnose → Rollback → Report. These operations are future work;
Phase 0 establishes the boundaries and checks needed to develop them safely.

## Architecture

- **Domain:** pure rules and value models (not yet implemented).
- **Application:** use cases and ports, referencing Domain.
- **Infrastructure:** future filesystem adapters, referencing Application/Domain.
- **App:** WPF views, future MVVM view models, and composition root.
- **Tests:** one xUnit project per non-UI layer, with architecture regression checks.

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

Development follows the [evidence-driven Graph Loop](docs/graph-loop.md).
See [AGENTS.md](AGENTS.md) for concise rules and [testing](docs/testing.md) for verification scope.

## Roadmap

1. Phase 0: solution, boundaries, tests, CI, documentation, minimal shell.
2. Phase 1: read-only Instance Inspector.
3. Separate later changes: Planner → Rules → Preview → Backup → Executor → Verifier → Report,
   with diagnosis and rollback failure paths.

Each node gets a focused issue/PR and its own acceptance tests before the next expansion.
Legacy selection candidates are documented in [migration rules](docs/migration-rules.md);
they are not implemented or promises of compatibility.
