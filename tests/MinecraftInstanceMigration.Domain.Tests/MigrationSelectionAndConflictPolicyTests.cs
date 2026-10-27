using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Domain.Tests;

public sealed class MigrationSelectionAndConflictPolicyTests
{
    [Fact]
    public void AllPresetContainsEveryKnownCandidateInCatalogOrder()
    {
        Assert.Equal(11, MigrationSelectionPresets.All.Count);
        Assert.Equal(
            KnownEntryCatalog.All.Select(entry => entry.Name),
            MigrationSelectionPresets.All);
    }

    [Fact]
    public void RecommendedPresetMatchesLegacyDirectionAndKeepsWorldsAndScreenshotsOptIn()
    {
        Assert.Equal(
            new[]
            {
                "options.txt", "config", "resourcepacks", "shaderpacks", "schematics",
                "XaeroWaypoints", "XaeroWorldMap", "itemscroller", "g4mespeed",
            },
            MigrationSelectionPresets.Recommended);
        Assert.DoesNotContain("saves", MigrationSelectionPresets.Recommended);
        Assert.DoesNotContain("screenshots", MigrationSelectionPresets.Recommended);
    }

    [Fact]
    public void ConflictRemainsUnresolvedWithoutExplicitDecision()
    {
        var plan = MigrationPlanPolicy.Create(
            Inspection(("config", EntryState.Directory)),
            Inspection(("config", EntryState.Directory)),
            ["config"]);

        Assert.Equal(MigrationPlanStatus.NeedsDecision, plan.Status);
        Assert.Equal(MigrationPlanDisposition.DestinationConflict, Entry(plan, "config").Disposition);
        Assert.Empty(plan.ConflictDecisionIssues);
    }

    [Fact]
    public void ExplicitSkipTurnsConflictIntoVisibleNoOp()
    {
        var plan = MigrationPlanPolicy.Create(
            Inspection(("config", EntryState.Directory)),
            Inspection(("config", EntryState.Directory)),
            ["config"],
            new Dictionary<string, DestinationConflictDecision>
            {
                ["config"] = DestinationConflictDecision.Skip,
            });

        Assert.Equal(MigrationPlanStatus.Ready, plan.Status);
        Assert.Equal(MigrationPlanDisposition.SkippedDestinationConflict, Entry(plan, "config").Disposition);
        Assert.Equal(1, plan.SkippedConflictCount);
        Assert.Equal(0, plan.ReadyForWriteCount);
    }

    [Fact]
    public void ExplicitReplaceProducesBackupRequiringIntentWithoutPerformingWrite()
    {
        var plan = MigrationPlanPolicy.Create(
            Inspection(("config", EntryState.Directory)),
            Inspection(("config", EntryState.Directory)),
            ["config"],
            new Dictionary<string, DestinationConflictDecision>
            {
                ["config"] = DestinationConflictDecision.Replace,
            });

        Assert.Equal(MigrationPlanStatus.Ready, plan.Status);
        MigrationPlanEntry config = Entry(plan, "config");
        Assert.Equal(MigrationPlanDisposition.ReadyToReplace, config.Disposition);
        Assert.True(config.RequiresBackup);
        Assert.True(config.IsReadyForWrite);
        Assert.Equal(1, plan.ReadyToReplaceCount);
    }

    [Theory]
    [InlineData("unknown", ConflictDecisionIssueKind.UnknownEntry)]
    [InlineData("saves", ConflictDecisionIssueKind.NotSelected)]
    public void InvalidDecisionTargetBlocksPlan(string name, ConflictDecisionIssueKind issueKind)
    {
        var plan = MigrationPlanPolicy.Create(
            Inspection(("config", EntryState.Directory)),
            Inspection(("config", EntryState.Directory)),
            ["config"],
            new Dictionary<string, DestinationConflictDecision>
            {
                [name] = DestinationConflictDecision.Skip,
            });

        Assert.Equal(MigrationPlanStatus.Blocked, plan.Status);
        Assert.Contains(plan.ConflictDecisionIssues, issue => issue.Name == name && issue.Kind == issueKind);
    }

    [Fact]
    public void StaleDecisionForNonConflictBlocksInsteadOfBeingSilentlyIgnored()
    {
        var plan = MigrationPlanPolicy.Create(
            Inspection(("config", EntryState.Directory)),
            Inspection(),
            ["config"],
            new Dictionary<string, DestinationConflictDecision>
            {
                ["config"] = DestinationConflictDecision.Replace,
            });

        Assert.Equal(MigrationPlanStatus.Blocked, plan.Status);
        Assert.Contains(plan.ConflictDecisionIssues,
            issue => issue.Name == "config" && issue.Kind == ConflictDecisionIssueKind.NoDestinationConflict);
        Assert.Equal(MigrationPlanDisposition.ReadyToCopy, Entry(plan, "config").Disposition);
    }

    [Fact]
    public void UnsupportedEnumValueBlocksPlanAndLeavesConflictUnresolved()
    {
        var plan = MigrationPlanPolicy.Create(
            Inspection(("config", EntryState.Directory)),
            Inspection(("config", EntryState.Directory)),
            ["config"],
            new Dictionary<string, DestinationConflictDecision>
            {
                ["config"] = (DestinationConflictDecision)999,
            });

        Assert.Equal(MigrationPlanStatus.Blocked, plan.Status);
        Assert.Contains(plan.ConflictDecisionIssues,
            issue => issue.Name == "config" && issue.Kind == ConflictDecisionIssueKind.UnsupportedDecision);
        Assert.Equal(MigrationPlanDisposition.DestinationConflict, Entry(plan, "config").Disposition);
    }

    [Fact]
    public void SourceBlockerWinsEvenWhenCallerSuppliesReplaceDecision()
    {
        var plan = MigrationPlanPolicy.Create(
            Inspection(("config", EntryState.ReparsePoint)),
            Inspection(("config", EntryState.Directory)),
            ["config"],
            new Dictionary<string, DestinationConflictDecision>
            {
                ["config"] = DestinationConflictDecision.Replace,
            });

        Assert.Equal(MigrationPlanStatus.Blocked, plan.Status);
        Assert.Equal(MigrationPlanDisposition.BlockedSourceReparsePoint, Entry(plan, "config").Disposition);
        Assert.Contains(plan.ConflictDecisionIssues,
            issue => issue.Name == "config" && issue.Kind == ConflictDecisionIssueKind.NoDestinationConflict);
    }

    // The executor replaces only an entry of the catalog kind, so a mismatched Replace must be refused
    // during planning: before any Backup IO or durable Started record could be produced.
    [Theory]
    [InlineData("config", EntryState.Directory, EntryState.File)]
    [InlineData("options.txt", EntryState.File, EntryState.Directory)]
    public void ReplaceOfDestinationWithDifferentKindIsBlockedBeforeBackup(
        string name,
        EntryState sourceState,
        EntryState destinationState)
    {
        var plan = MigrationPlanPolicy.Create(
            Inspection((name, sourceState)),
            Inspection((name, destinationState)),
            [name],
            new Dictionary<string, DestinationConflictDecision>
            {
                [name] = DestinationConflictDecision.Replace,
            });

        MigrationPlanEntry entry = Entry(plan, name);
        Assert.Equal(MigrationPlanStatus.Blocked, plan.Status);
        Assert.Equal(MigrationPlanDisposition.BlockedDestinationKindMismatch, entry.Disposition);
        Assert.True(entry.IsBlocked);
        Assert.False(entry.RequiresBackup);
        Assert.Empty(plan.ConflictDecisionIssues);
        Assert.Equal(0, plan.ReadyForWriteCount);
        Assert.Equal(Backup.BackupPlanStatus.Blocked, Backup.BackupPlanPolicy.Create(plan).Status);
        Assert.Equal(Execution.ExecutionJournalStatus.Blocked, Execution.ExecutionJournalPolicy.Create(plan).Status);
    }

    [Theory]
    [InlineData(DestinationConflictDecision.Unresolved, MigrationPlanDisposition.DestinationConflict, MigrationPlanStatus.NeedsDecision)]
    [InlineData(DestinationConflictDecision.Skip, MigrationPlanDisposition.SkippedDestinationConflict, MigrationPlanStatus.Ready)]
    public void DestinationWithDifferentKindCanStillBeLeftUntouched(
        DestinationConflictDecision decision,
        MigrationPlanDisposition expectedDisposition,
        MigrationPlanStatus expectedStatus)
    {
        var plan = MigrationPlanPolicy.Create(
            Inspection(("config", EntryState.Directory)),
            Inspection(("config", EntryState.File)),
            ["config"],
            new Dictionary<string, DestinationConflictDecision>
            {
                ["config"] = decision,
            });

        Assert.Equal(expectedStatus, plan.Status);
        Assert.Equal(expectedDisposition, Entry(plan, "config").Disposition);
        Assert.Empty(plan.ConflictDecisionIssues);
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
