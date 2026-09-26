using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Application.Inspection;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Tests;

public sealed class ExecutionLiveValidatorTests
{
    [Fact]
    public async Task InspectsSourceThenDestinationAndValidatesReviewedStep()
    {
        var calls = new List<string>();
        var inspector = new StubInspector((path, _) =>
        {
            calls.Add(path);
            return Task.FromResult(path == "source"
                ? Inspection(("config", EntryState.Directory))
                : Inspection(("config", EntryState.Directory)));
        });
        var validator = new ExecutionLiveValidator(inspector);
        MigrationPlan plan = Plan();
        var step = new ExecutionJournalEntry(
            0,
            "config",
            ExpectedEntryKind.Directory,
            ExecutionOperationKind.Replace);

        ExecutionLiveValidationResult result = await validator.ValidateStepAsync(
            "source",
            "destination",
            plan,
            step,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Equal(new[] { "source", "destination" }, calls);
    }

    private static MigrationPlan Plan() =>
        new(
            EntryState.Directory,
            EntryState.Directory,
            [
                new MigrationPlanEntry(
                    "config",
                    ExpectedEntryKind.Directory,
                    EntryState.Directory,
                    EntryState.Directory,
                    true,
                    MigrationPlanDisposition.ReadyToReplace),
            ],
            []);

    private static InstanceInspectionResult Inspection(
        params (string Name, EntryState State)[] entries) =>
        new(
            EntryState.Directory,
            entries.Select(entry =>
                new EntryObservation(
                    entry.Name,
                    ExpectedEntryKind.Directory,
                    entry.State)));

    private sealed class StubInspector(
        Func<string, CancellationToken, Task<InstanceInspectionResult>> run)
        : IInstanceInspector
    {
        public Task<InstanceInspectionResult> InspectAsync(
            string candidatePath,
            CancellationToken cancellationToken = default) =>
            run(candidatePath, cancellationToken);
    }
}
