# ADR 0001: .NET 10, WPF, and inward dependencies

- Status: Accepted for Phase 0
- Date: 2026-09-26

## Context

The target is a Windows desktop tool for safe, selective Minecraft user-data migration.
The foundation must support deterministic tests independently of UI and actual file operations.
Phase 0 must not implement migration behavior.

## Decision

Use C# with .NET 10 LTS and WPF. Pin the SDK feature band through `global.json` and permit
servicing patches only. Use `.slnx` for the seven-project solution.
Enable nullable, deterministic builds, SDK analyzers, and warnings as errors.

Separate Domain, Application, Infrastructure, and App. Dependency direction is documented
in [architecture](../architecture.md); App may reference Infrastructure at composition.
Libraries target `net10.0`, App targets `net10.0-windows`.

Use MVVM for future interaction. A static Phase 0 shell does not need a view model,
binding helper, command implementation, DI container, or third-party MVVM package.

Use xUnit v3 with VSTest integration for the standard `dotnet test` CLI.
Centralize test package versions in `Directory.Packages.props`; production has no NuGet packages.
Use BCL metadata readers and evaluated MSBuild queries for architecture guards.
Run restore, Release build, all tests, and format verification on Windows GitHub Actions.

Adopt the evidence-driven Graph Loop with context-routed documentation and explicit human gates.

## Alternatives considered

- One WPF project: simpler initially, but invites UI/business-rule/filesystem coupling.
- Cross-platform UI: adds scope without a cross-platform product requirement.
- Early MVVM/DI/architecture libraries: defer until a real use case demonstrates value.
- Placeholder rules and filesystem interfaces: defer to the node that defines their contracts.

## Consequences

Local full-solution verification needs Windows and the selected SDK; NuGet is needed on first
restore. SDK/package updates are deliberate changes that re-run the foundation checks.
Windows CI configuration is not proof of a hosted run until a remote workflow actually completes.

The empty library assemblies intentionally contain no invented public APIs. Architecture tests
protect some structural constraints, but safety of future migrations needs substantial additional
unit/integration/failure tests. No migration safety or compatibility claim follows from Phase 0.

## Sources

- [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [xUnit v3 documentation](https://xunit.net/docs/getting-started/v3/getting-started)
