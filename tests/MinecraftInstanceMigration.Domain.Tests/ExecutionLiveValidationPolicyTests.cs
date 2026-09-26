using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Domain.Tests;

public sealed class ExecutionLiveValidationPolicyTests
{
    [Fact]
    public void UnchangedCopyStepIsValid()
    {
        MigrationPlan plan = Plan(
            Entry("options.txt", ExpectedEntryKind.File, EntryState.File, EntryState.Missing, MigrationPlanDisposition.ReadyToCopy));
        var step = new ExecutionJournalEntry(
            0,
            "options.txt",
            ExpectedEntryKind.File,
            ExecutionOperationKind.Copy);

        ExecutionLiveValidationResult result =
            ExecutionLiveValidationPolicy.ValidateStep(
                plan,
                step,
                Inspection(("options.txt", ExpectedEntryKind.File, EntryState.File)),
                Inspection(("options.txt", ExpectedEntryKind.File, EntryState.Missing)));

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void DestinationAppearingBeforeCopyInvalidatesStep()
    {
        MigrationPlan plan = Plan(
            Entry("options.txt", ExpectedEntryKind.File, EntryState.File, EntryState.Missing, MigrationPlanDisposition.ReadyToCopy));
        var step = new ExecutionJournalEntry(
            0,
            "options.txt",
            ExpectedEntryKind.File,
            ExecutionOperationKind.Copy);

        ExecutionLiveValidationResult result =
            ExecutionLiveValidationPolicy.ValidateStep(
                plan,
                step,
                Inspection(("options.txt", ExpectedEntryKind.File, EntryState.File)),
                Inspection(("options.txt", ExpectedEntryKind.File, EntryState.File)));

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue => issue.Kind == ExecutionLiveValidationIssueKind.DestinationStateChanged);
    }

    [Fact]
    public void SourceKindChangingBeforeReplaceInvalidatesStep()
    {
        MigrationPlan plan = Plan(
            Entry("config", ExpectedEntryKind.Directory, EntryState.Directory, EntryState.Directory, MigrationPlanDisposition.ReadyToReplace));
        var step = new ExecutionJournalEntry(
            0,
            "config",
            ExpectedEntryKind.Directory,
            ExecutionOperationKind.Replace);

        ExecutionLiveValidationResult result =
            ExecutionLiveValidationPolicy.ValidateStep(
                plan,
                step,
                Inspection(("config", ExpectedEntryKind.Directory, EntryState.File)),
                Inspection(("config", ExpectedEntryKind.Directory, EntryState.Directory)));

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue => issue.Kind == ExecutionLiveValidationIssueKind.SourceStateChanged);
    }

    [Fact]
    public void UnsafeRootInvalidatesStep()
    {
        MigrationPlan plan = Plan(
            Entry("config", ExpectedEntryKind.Directory, EntryState.Directory, EntryState.Directory, MigrationPlanDisposition.ReadyToReplace));
        var step = new ExecutionJournalEntry(
            0,
            "config",
            ExpectedEntryKind.Directory,
            ExecutionOperationKind.Replace);

        ExecutionLiveValidationResult result =
            ExecutionLiveValidationPolicy.ValidateStep(
                plan,
                step,
                new InstanceInspectionResult(EntryState.ReparsePoint, []),
                Inspection(("config", ExpectedEntryKind.Directory, EntryState.Directory)));

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Issues,
            issue => issue.Kind == ExecutionLiveValidationIssueKind.SourceRootChanged);
    }

    [Fact]
    public void JournalStepMustMatchReviewedWriteIntent()
    {
        MigrationPlan plan = Plan(
            Entry("config", ExpectedEntryKind.Directory, EntryState.Directory, EntryState.Directory, MigrationPlanDisposition.ReadyToReplace));
        var wrongStep = new ExecutionJournalEntry(
            0,
            "config",
            ExpectedEntryKind.Directory,
            ExecutionOperationKind.Copy);

        ExecutionLiveValidationResult result =
            ExecutionLiveValidationPolicy.ValidateStep(
                plan,
                wrongStep,
                Inspection(("config", ExpectedEntryKind.Directory, EntryState.Directory)),
                Inspection(("config", ExpectedEntryKind.Directory, EntryState.Directory)));

        Assert.False(result.IsValid);
        Assert.Equal(
            ExecutionLiveValidationIssueKind.JournalStepMismatch,
            Assert.Single(result.Issues).Kind);
    }

    private static MigrationPlan Plan(params MigrationPlanEntry[] entries) =>
        new(EntryState.Directory, EntryState.Directory, entries, []);

    private static MigrationPlanEntry Entry(
        string name,
        ExpectedEntryKind expectedKind,
        EntryState source,
        EntryState destination,
        MigrationPlanDisposition disposition) =>
        new(name, expectedKind, source, destination, true, disposition);

    private static InstanceInspectionResult Inspection(
        params (string Name, ExpectedEntryKind Kind, EntryState State)[] entries) =>
        new(
            EntryState.Directory,
            entries.Select(entry =>
                new EntryObservation(entry.Name, entry.Kind, entry.State)));
}
