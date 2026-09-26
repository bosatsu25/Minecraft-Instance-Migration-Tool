using MinecraftInstanceMigration.Application.Planning;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Tests;

public sealed class MigrationPlannerPresetTests
{
    [Fact]
    public void RecommendedPlanUsesDomainPresetWithoutSelectingWorldsOrScreenshots()
    {
        var source = Inspection(
            ("config", EntryState.Directory),
            ("saves", EntryState.Directory),
            ("screenshots", EntryState.Directory));
        var destination = Inspection();

        MigrationPlan plan = new MigrationPlanner().CreateRecommendedPlan(source, destination);

        Assert.True(Entry(plan, "config").Selected);
        Assert.Equal(MigrationPlanDisposition.ReadyToCopy, Entry(plan, "config").Disposition);
        Assert.False(Entry(plan, "saves").Selected);
        Assert.False(Entry(plan, "screenshots").Selected);
    }

    [Fact]
    public void ApplicationPlannerPassesExplicitConflictDecisionToDomainPolicy()
    {
        var source = Inspection(("config", EntryState.Directory));
        var destination = Inspection(("config", EntryState.Directory));

        MigrationPlan plan = new MigrationPlanner().CreatePlan(
            source,
            destination,
            ["config"],
            new Dictionary<string, DestinationConflictDecision>
            {
                ["config"] = DestinationConflictDecision.Skip,
            });

        Assert.Equal(MigrationPlanStatus.Ready, plan.Status);
        Assert.Equal(MigrationPlanDisposition.SkippedDestinationConflict, Entry(plan, "config").Disposition);
    }

    private static MigrationPlanEntry Entry(MigrationPlan plan, string name) =>
        Assert.Single(plan.Entries, entry => entry.Name == name);

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
