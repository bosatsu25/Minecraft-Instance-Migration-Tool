using MinecraftInstanceMigration.Application.Planning;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Tests;

public sealed class MigrationPlannerTests
{
    [Fact]
    public void ApplicationPlannerExposesDeterministicDomainPlanWithoutFilesystemAccess()
    {
        var source = Inspection(("options.txt", EntryState.File));
        var destination = Inspection();

        MigrationPlan plan = new MigrationPlanner().CreatePlan(source, destination, ["options.txt"]);

        Assert.Equal(MigrationPlanStatus.Ready, plan.Status);
        var entry = Assert.Single(plan.Entries, item => item.Name == "options.txt");
        Assert.Equal(MigrationPlanDisposition.ReadyToCopy, entry.Disposition);
    }

    private static InstanceInspectionResult Inspection(params (string Name, EntryState State)[] overrides)
    {
        var states = overrides.ToDictionary(item => item.Name, item => item.State, StringComparer.Ordinal);
        return new InstanceInspectionResult(
            EntryState.Directory,
            KnownEntryCatalog.All.Select(candidate =>
                new EntryObservation(
                    candidate.Name,
                    candidate.ExpectedKind,
                    states.GetValueOrDefault(candidate.Name, EntryState.Missing))));
    }
}
