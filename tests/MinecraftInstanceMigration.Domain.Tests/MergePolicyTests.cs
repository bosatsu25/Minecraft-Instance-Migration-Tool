using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Domain.Tests;

// ADR 0004: Merge is an explicit decision whose plan readiness depends on read-only inspection evidence.
public sealed class MergePolicyTests
{
    [Theory]
    [InlineData("options.txt", MergeUnit.NotOffered)]
    [InlineData("config", MergeUnit.File)]
    [InlineData("resourcepacks", MergeUnit.TopLevelChild)]
    [InlineData("shaderpacks", MergeUnit.TopLevelChild)]
    [InlineData("schematics", MergeUnit.File)]
    [InlineData("saves", MergeUnit.NotOffered)]
    [InlineData("screenshots", MergeUnit.File)]
    [InlineData("XaeroWaypoints", MergeUnit.TopLevelChild)]
    [InlineData("XaeroWorldMap", MergeUnit.TopLevelChild)]
    [InlineData("itemscroller", MergeUnit.File)]
    [InlineData("g4mespeed", MergeUnit.File)]
    public void CatalogPinsTheApprovedMergeUnits(string name, MergeUnit expected)
    {
        Assert.Equal(expected, Assert.Single(KnownEntryCatalog.All, entry => entry.Name == name).MergeUnit);
    }

    [Fact]
    public void OnlyDirectoryCandidatesOfferMerge()
    {
        Assert.All(
            KnownEntryCatalog.All.Where(entry => entry.MergeUnit != MergeUnit.NotOffered),
            entry => Assert.Equal(ExpectedEntryKind.Directory, entry.ExpectedKind));
    }

    [Fact]
    public void MergeWithoutInspectionEvidenceWaitsWithoutBecomingReady()
    {
        MigrationPlan plan = Plan(mergeInspections: null);
        MigrationPlanEntry entry = Entry(plan, "config");

        Assert.Equal(MigrationPlanDisposition.MergeInspectionRequired, entry.Disposition);
        Assert.True(entry.NeedsMergeInspection);
        Assert.False(entry.NeedsDecision);
        Assert.False(entry.IsReadyForWrite);
        Assert.False(entry.IsBlocked);
        Assert.Null(entry.MergeInspection);
        Assert.Equal(MigrationPlanStatus.NeedsDecision, plan.Status);
        Assert.Equal(1, plan.MergeInspectionRequiredCount);
        Assert.Empty(plan.ConflictDecisionIssues);
        Assert.Equal(BackupPlanStatus.Blocked, BackupPlanPolicy.Create(plan).Status);
        Assert.Equal(ExecutionJournalStatus.Blocked, ExecutionJournalPolicy.Create(plan).Status);
    }

    [Fact]
    public void CleanInspectionMakesMergeReadyAndBackupRequiring()
    {
        MergeInspectionResult inspection = Clean("config", MergeUnit.File);
        MigrationPlan plan = Plan(Evidence(inspection));
        MigrationPlanEntry entry = Entry(plan, "config");

        Assert.Equal(MigrationPlanStatus.Ready, plan.Status);
        Assert.Equal(MigrationPlanDisposition.ReadyToMerge, entry.Disposition);
        Assert.True(entry.IsReadyToMerge);
        Assert.True(entry.IsReadyForWrite);
        Assert.True(entry.RequiresBackup);
        Assert.Same(inspection, entry.MergeInspection);
        Assert.Equal(1, plan.ReadyToMergeCount);
        Assert.Equal(1, plan.ReadyForWriteCount);
    }

    [Theory]
    [InlineData(MergeInspectionStatus.KindConflict)]
    [InlineData(MergeInspectionStatus.ReparsePoint)]
    [InlineData(MergeInspectionStatus.Unavailable)]
    public void UncleanInspectionBlocksMergeButKeepsEvidence(MergeInspectionStatus status)
    {
        var inspection = new MergeInspectionResult("config", MergeUnit.File, status, 1, 0, 0, 1);
        MigrationPlan plan = Plan(Evidence(inspection));
        MigrationPlanEntry entry = Entry(plan, "config");

        Assert.Equal(MigrationPlanStatus.Blocked, plan.Status);
        Assert.Equal(MigrationPlanDisposition.BlockedMergeConflict, entry.Disposition);
        Assert.True(entry.IsBlocked);
        Assert.False(entry.RequiresBackup);
        Assert.Same(inspection, entry.MergeInspection);
    }

    [Theory]
    [InlineData("config", MergeUnit.TopLevelChild)]
    [InlineData("resourcepacks", MergeUnit.File)]
    public void EvidenceForAnotherEntryOrUnitCannotMakeMergeReady(string evidenceName, MergeUnit evidenceUnit)
    {
        MigrationPlan plan = Plan(new Dictionary<string, MergeInspectionResult>
        {
            ["config"] = Clean(evidenceName, evidenceUnit),
        });

        Assert.Equal(MigrationPlanDisposition.BlockedMergeConflict, Entry(plan, "config").Disposition);
        Assert.Equal(MigrationPlanStatus.Blocked, plan.Status);
    }

    [Theory]
    [InlineData("options.txt", EntryState.File)]
    [InlineData("saves", EntryState.Directory)]
    public void MergeOnCandidateWithoutMergeUnitIsAnUnsupportedDecision(string name, EntryState state)
    {
        MigrationPlan plan = MigrationPlanPolicy.Create(
            Inspection((name, state)),
            Inspection((name, state)),
            [name],
            new Dictionary<string, DestinationConflictDecision> { [name] = DestinationConflictDecision.Merge });

        Assert.Equal(MigrationPlanStatus.Blocked, plan.Status);
        Assert.Equal(MigrationPlanDisposition.DestinationConflict, Entry(plan, name).Disposition);
        ConflictDecisionIssue issue = Assert.Single(plan.ConflictDecisionIssues);
        Assert.Equal(ConflictDecisionIssueKind.UnsupportedDecision, issue.Kind);
    }

    [Fact]
    public void MergeOfDestinationWithDifferentKindIsBlockedBeforeInspection()
    {
        MigrationPlan plan = MigrationPlanPolicy.Create(
            Inspection(("config", EntryState.Directory)),
            Inspection(("config", EntryState.File)),
            ["config"],
            MergeDecision("config"),
            Evidence(Clean("config", MergeUnit.File)));

        Assert.Equal(MigrationPlanDisposition.BlockedDestinationKindMismatch, Entry(plan, "config").Disposition);
        Assert.Null(Entry(plan, "config").MergeInspection);
    }

    [Fact]
    public void MergeDecisionWithoutExistingDestinationIsReportedAsStale()
    {
        MigrationPlan plan = MigrationPlanPolicy.Create(
            Inspection(("config", EntryState.Directory)),
            Inspection(),
            ["config"],
            MergeDecision("config"));

        Assert.Equal(MigrationPlanDisposition.ReadyToCopy, Entry(plan, "config").Disposition);
        Assert.Equal(
            ConflictDecisionIssueKind.NoDestinationConflict,
            Assert.Single(plan.ConflictDecisionIssues).Kind);
    }

    [Fact]
    public void InspectionEvidenceWithoutMergeDecisionIsIgnored()
    {
        MigrationPlan plan = MigrationPlanPolicy.Create(
            Inspection(("config", EntryState.Directory)),
            Inspection(("config", EntryState.Directory)),
            ["config"],
            mergeInspections: Evidence(Clean("config", MergeUnit.File)));

        Assert.Equal(MigrationPlanDisposition.DestinationConflict, Entry(plan, "config").Disposition);
        Assert.Null(Entry(plan, "config").MergeInspection);
    }

    [Fact]
    public void ReadyMergeMapsToMergePreviewAndBlockedOrPendingMergeDoNot()
    {
        MigrationPreview ready = MigrationPreviewPolicy.Create(Plan(Evidence(Clean("config", MergeUnit.File))));
        MigrationPreview pending = MigrationPreviewPolicy.Create(Plan(mergeInspections: null));
        MigrationPreview blocked = MigrationPreviewPolicy.Create(Plan(Evidence(
            new MergeInspectionResult("config", MergeUnit.File, MergeInspectionStatus.KindConflict, 0, 0, 0, 0))));

        MigrationPreviewEntry readyEntry = Assert.Single(ready.Entries, entry => entry.Name == "config");
        Assert.Equal(MigrationPreviewAction.Merge, readyEntry.Action);
        Assert.True(readyEntry.RequiresBackup);
        Assert.True(ready.HasPlannedWrites);
        Assert.Equal(
            MigrationPreviewAction.NeedsDecision,
            Assert.Single(pending.Entries, entry => entry.Name == "config").Action);
        Assert.Equal(
            MigrationPreviewAction.Blocked,
            Assert.Single(blocked.Entries, entry => entry.Name == "config").Action);
    }

    [Fact]
    public void ReadyMergeIsBackedUpAsTheWholeDestinationEntry()
    {
        BackupPlan backup = BackupPlanPolicy.Create(Plan(Evidence(Clean("config", MergeUnit.File))));

        Assert.Equal(BackupPlanStatus.Ready, backup.Status);
        BackupPlanEntry entry = Assert.Single(backup.Entries);
        Assert.Equal("config", entry.Name);
        Assert.Equal(ExpectedEntryKind.Directory, entry.ExpectedKind);
        Assert.Equal(EntryState.Directory, entry.DestinationState);
    }

    [Fact]
    public void ReadyMergeBecomesAMergeJournalStepThatLiveValidationAccepts()
    {
        MigrationPlan plan = Plan(Evidence(Clean("config", MergeUnit.File)));

        ExecutionJournalDraft draft = ExecutionJournalPolicy.Create(plan);

        Assert.Equal(ExecutionJournalStatus.Ready, draft.Status);
        ExecutionJournalEntry step = Assert.Single(draft.Entries);
        Assert.Equal(ExecutionOperationKind.Merge, step.Operation);
        Assert.Equal(
            ExecutionLiveValidationStatus.Valid,
            ExecutionLiveValidationPolicy.ValidateStep(plan, step, Live(), Live()).Status);
        Assert.Equal(
            ExecutionLiveValidationStatus.Invalid,
            ExecutionLiveValidationPolicy.ValidateStep(
                plan,
                step with { Operation = ExecutionOperationKind.Replace },
                Live(),
                Live()).Status);
    }

    [Fact]
    public void MalformedMergeIntentWithoutCleanEvidenceFailsClosed()
    {
        var entry = new MigrationPlanEntry(
            "config",
            ExpectedEntryKind.Directory,
            EntryState.Directory,
            EntryState.Directory,
            true,
            MigrationPlanDisposition.ReadyToMerge);
        var plan = new MigrationPlan(EntryState.Directory, EntryState.Directory, [entry], []);

        ExecutionJournalDraft draft = ExecutionJournalPolicy.Create(plan);

        Assert.Equal(ExecutionJournalStatus.Blocked, draft.Status);
        Assert.Equal(
            ExecutionJournalBlockerKind.InvalidWriteIntent,
            Assert.Single(draft.Blockers).Kind);
    }

    [Theory]
    [InlineData(true, RollbackActionKind.RestoreFromBackup, RollbackPlanStatus.Ready)]
    [InlineData(false, RollbackActionKind.ManualRecoveryRequired, RollbackPlanStatus.RecoveryRequired)]
    public void AppliedMergeRestoresFromBackupOnlyWhenBackupIsValid(
        bool validatedBackupAvailable,
        RollbackActionKind expectedAction,
        RollbackPlanStatus expectedStatus)
    {
        var fingerprint = new ExecutionContentFingerprint(1, 1, 10, new string('A', 64));
        var draft = new ExecutionJournalDraft(
            ExecutionJournalStatus.Ready,
            [new ExecutionJournalEntry(0, "config", ExpectedEntryKind.Directory, ExecutionOperationKind.Merge)],
            []);
        var snapshot = new ExecutionJournalSnapshot(
            ExecutionJournalDraft.CurrentSchemaVersion,
            [new ExecutionJournalStep(0, "config", ExecutionOperationKind.Merge, ExecutionStepOutcome.Applied, fingerprint)]);

        RollbackPlan rollback = RollbackPlanPolicy.Create(draft, snapshot, validatedBackupAvailable);

        Assert.Equal(expectedStatus, rollback.Status);
        RollbackPlanEntry action = Assert.Single(rollback.Entries);
        Assert.Equal(expectedAction, action.Action);
        Assert.Equal(ExecutionOperationKind.Merge, action.Operation);
        Assert.Equal(fingerprint, action.ExpectedCurrentFingerprint);
    }

    [Fact]
    public void InspectionEvidenceRejectsImpossibleValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MergeInspectionResult("config", MergeUnit.NotOffered, MergeInspectionStatus.Clean, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MergeInspectionResult("config", MergeUnit.File, MergeInspectionStatus.Clean, -1, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MergeInspectionResult("config", MergeUnit.File, MergeInspectionStatus.Clean, 0, 0, 0, -1));
        Assert.Throws<ArgumentException>(() =>
            new MergeInspectionResult(" ", MergeUnit.File, MergeInspectionStatus.Clean, 0, 0, 0, 0));
    }

    private static MigrationPlan Plan(IReadOnlyDictionary<string, MergeInspectionResult>? mergeInspections) =>
        MigrationPlanPolicy.Create(
            Inspection(("config", EntryState.Directory)),
            Inspection(("config", EntryState.Directory)),
            ["config"],
            MergeDecision("config"),
            mergeInspections);

    private static Dictionary<string, DestinationConflictDecision> MergeDecision(string name) =>
        new(StringComparer.Ordinal) { [name] = DestinationConflictDecision.Merge };

    private static Dictionary<string, MergeInspectionResult> Evidence(MergeInspectionResult inspection) =>
        new(StringComparer.Ordinal) { ["config"] = inspection };

    private static MergeInspectionResult Clean(string name, MergeUnit unit) =>
        new(name, unit, MergeInspectionStatus.Clean, 2, 1, 3, 1024);

    private static MigrationPlanEntry Entry(MigrationPlan plan, string name) =>
        Assert.Single(plan.Entries, entry => entry.Name == name);

    private static InstanceInspectionResult Live() => Inspection(("config", EntryState.Directory));

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
