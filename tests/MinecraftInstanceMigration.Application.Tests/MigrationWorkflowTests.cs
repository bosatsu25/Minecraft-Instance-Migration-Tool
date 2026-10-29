using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Application.Capacity;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Application.Inspection;
using MinecraftInstanceMigration.Application.Planning;
using MinecraftInstanceMigration.Application.Workflow;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Tests;

public sealed class MigrationWorkflowTests
{
    [Theory]
    [InlineData(ExecutionWorkspaceSafetyStatus.Invalid, false)]
    [InlineData(ExecutionWorkspaceSafetyStatus.Cancelled, false)]
    [InlineData(ExecutionWorkspaceSafetyStatus.Safe, true)]
    public async Task UnsafeOrUnavailableWorkspaceNeverInvokesBackup(
        ExecutionWorkspaceSafetyStatus workspaceStatus, bool throws)
    {
        var calls = new List<string>();
        MigrationWorkflow workflow = CreateWorkflow(
            backupPlanner: new StubBackupPlanner(RequiredBackupPlan()),
            backupExecutor: new StubBackupExecutor(calls),
            workspace: new StubWorkspaceValidator(workspaceStatus, throws));
        MigrationWorkflowSession session = await ReadyForBackup(workflow);
        session = workflow.PrepareBackup(session, "backups", "journals");
        session = await workflow.ExecuteBackupAsync(session, TestContext.Current.CancellationToken);
        Assert.Empty(calls);
        Assert.Equal(workspaceStatus == ExecutionWorkspaceSafetyStatus.Cancelled
            ? MigrationWorkflowFailureKind.Cancelled
            : MigrationWorkflowFailureKind.WorkspaceUnsafe, session.FailureKind);
        Assert.Null(session.BackupResult);
    }

    [Fact]
    public async Task BothBackupAndJournalParentsAreValidatedBeforeBackup()
    {
        var calls = new List<string>();
        MigrationWorkflow workflow = CreateWorkflow(
            backupPlanner: new StubBackupPlanner(RequiredBackupPlan()),
            backupExecutor: new StubBackupExecutor(calls),
            workspace: new StubWorkspaceValidator(calls: calls));
        MigrationWorkflowSession session = await ReadyForBackup(workflow);
        session = workflow.PrepareBackup(session, "backups", "journals");
        session = await workflow.ExecuteBackupAsync(session, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "workspace:backups", "workspace:journals", "backup" }, calls);
        Assert.Equal(MigrationWorkflowState.BackupReady, session.State);
    }

    [Fact]
    public void NewSessionRequiresRootSelection()
    {
        MigrationWorkflow workflow = CreateWorkflow();

        MigrationWorkflowSession session = workflow.CreateSession();

        Assert.Equal(MigrationWorkflowState.SelectRoots, session.State);
        Assert.Null(session.SourceRoot);
        Assert.Null(session.MigrationPlan);
    }

    [Fact]
    public void SelectingRootsMovesToInspection()
    {
        MigrationWorkflow workflow = CreateWorkflow();

        MigrationWorkflowSession session = workflow.SelectRoots(
            workflow.CreateSession(),
            "source",
            "destination");

        Assert.Equal(MigrationWorkflowState.Inspect, session.State);
        Assert.Equal("source", session.SourceRoot);
        Assert.Equal("destination", session.DestinationRoot);
        Assert.Null(session.FailureKind);
    }

    [Fact]
    public async Task InspectThenPreviewPublishesOrderedApplicationEvidence()
    {
        var calls = new List<string>();
        var source = Inspection();
        var destination = Inspection();
        var plan = ReadyPlan();
        var preview = new MigrationPreview(
            MigrationPlanStatus.Ready,
            [],
            [],
            []);
        MigrationWorkflow workflow = CreateWorkflow(
            new StubInspector(path =>
            {
                calls.Add(path);
                return path == "source" ? source : destination;
            }),
            new StubPlanner(plan),
            new StubPreviewer(preview));

        MigrationWorkflowSession session = workflow.SelectRoots(
            workflow.CreateSession(),
            "source",
            "destination");
        session = await workflow.InspectAsync(session, TestContext.Current.CancellationToken);
        session = workflow.ConfigurePlan(session, ["options.txt"], null);
        session = workflow.CreatePreview(session);

        Assert.Equal(new[] { "source", "destination" }, calls);
        Assert.Equal(MigrationWorkflowState.ReadyForBackup, session.State);
        Assert.Same(source, session.SourceInspection);
        Assert.Same(destination, session.DestinationInspection);
        Assert.Same(plan, session.MigrationPlan);
        Assert.Same(preview, session.MigrationPreview);
    }

    [Fact]
    public async Task PreviewThatNeedsDecisionDoesNotAdvanceToBackup()
    {
        var preview = new MigrationPreview(
            MigrationPlanStatus.NeedsDecision,
            [],
            [],
            [new ConflictDecisionIssue("config", ConflictDecisionIssueKind.NoDestinationConflict)]);
        MigrationWorkflow workflow = CreateWorkflow(
            planner: new StubPlanner(ReadyPlan()),
            previewer: new StubPreviewer(preview));

        MigrationWorkflowSession session = await InspectedSession(workflow);
        session = workflow.ConfigurePlan(session, ["options.txt"], null);
        session = workflow.CreatePreview(session);

        Assert.Equal(MigrationWorkflowState.Preview, session.State);
        Assert.Equal(MigrationWorkflowFailureKind.PlanNotReady, session.FailureKind);
        Assert.Null(session.BackupPlan);
    }

    [Fact]
    public async Task BackupAndExecutionAdvanceOnlyAfterDurablePrerequisites()
    {
        var calls = new List<string>();
        MigrationWorkflow workflow = CreateWorkflow(
            planner: new StubPlanner(ReadyPlan()),
            previewer: new StubPreviewer(new MigrationPreview(
                MigrationPlanStatus.Ready, [], [], [])),
            backupPlanner: new StubBackupPlanner(RequiredBackupPlan()),
            backupExecutor: new StubBackupExecutor(calls),
            execution: new StubExecutionOrchestrator(calls));

        MigrationWorkflowSession session = await ReadyForBackup(workflow);
        session = workflow.PrepareBackup(session, "backups", "journals");
        Assert.Equal(MigrationWorkflowState.ReadyForBackup, session.State);
        session = await workflow.ExecuteBackupAsync(session, TestContext.Current.CancellationToken);
        Assert.Equal(MigrationWorkflowState.BackupReady, session.State);
        session = workflow.PrepareExecution(session);
        Assert.Equal(MigrationWorkflowState.ReadyForExecution, session.State);
        session = await workflow.ExecuteAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal(MigrationWorkflowState.Completed, session.State);
        Assert.Equal(new[] { "backup", "execute" }, calls);
    }

    [Fact]
    public async Task NotRequiredBackupAdvancesWithoutInvokingBackupExecutor()
    {
        var calls = new List<string>();
        MigrationWorkflow workflow = CreateWorkflow(
            backupExecutor: new StubBackupExecutor(calls));

        MigrationWorkflowSession session = await ReadyForBackup(workflow);
        session = workflow.PrepareBackup(session, backupParent: null, journalParent: "journals");
        session = await workflow.ExecuteBackupAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal(MigrationWorkflowState.BackupReady, session.State);
        Assert.Equal(BackupExecutionStatus.NotRequired, session.BackupResult?.Status);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task RequiredBackupRejectsMissingBackupParent()
    {
        MigrationWorkflow workflow = CreateWorkflow(
            backupPlanner: new StubBackupPlanner(RequiredBackupPlan()));

        MigrationWorkflowSession session = await ReadyForBackup(workflow);
        session = workflow.PrepareBackup(session, backupParent: null, journalParent: "journals");

        Assert.Equal(MigrationWorkflowState.ReadyForBackup, session.State);
        Assert.Equal(MigrationWorkflowFailureKind.BackupRequired, session.FailureKind);
        Assert.NotNull(session.BackupPlan);
    }

    [Fact]
    public async Task ReselectingRootsStartsAFreshSessionAfterCompletion()
    {
        MigrationWorkflow workflow = CreateWorkflow();
        MigrationWorkflowSession session = await ReadyForBackup(workflow);
        session = workflow.PrepareBackup(session, backupParent: null, journalParent: "journals");
        session = await workflow.ExecuteBackupAsync(session, TestContext.Current.CancellationToken);
        session = workflow.PrepareExecution(session);
        session = await workflow.ExecuteAsync(session, TestContext.Current.CancellationToken);

        session = workflow.SelectRoots(session, "new-source", "new-destination");

        Assert.Equal(MigrationWorkflowState.Inspect, session.State);
        Assert.Equal("new-source", session.SourceRoot);
        Assert.Equal("new-destination", session.DestinationRoot);
        Assert.Null(session.SourceInspection);
        Assert.Null(session.DestinationInspection);
        Assert.Null(session.MigrationPlan);
        Assert.Null(session.BackupResult);
        Assert.Null(session.ExecutionResult);
        Assert.Null(session.FailureKind);
    }

    [Fact]
    public async Task RecoveryRequiredExecutionStopsInRecoveryState()
    {
        MigrationWorkflow workflow = CreateWorkflow(
            planner: new StubPlanner(ReadyPlan()),
            previewer: new StubPreviewer(new MigrationPreview(
                MigrationPlanStatus.Ready, [], [], [])),
            backupPlanner: new StubBackupPlanner(RequiredBackupPlan()),
            backupExecutor: new StubBackupExecutor([]),
            execution: new StubExecutionOrchestrator([], ExecutionOrchestrationStatus.RecoveryRequired));

        MigrationWorkflowSession session = await ReadyForBackup(workflow);
        session = workflow.PrepareBackup(session, "backups", "journals");
        session = await workflow.ExecuteBackupAsync(session, TestContext.Current.CancellationToken);
        session = workflow.PrepareExecution(session);
        session = await workflow.ExecuteAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal(MigrationWorkflowState.RecoveryRequired, session.State);
        Assert.Equal(MigrationWorkflowFailureKind.ExecutionRecoveryRequired, session.FailureKind);
    }

    [Fact]
    public async Task CancellationDuringInspectionDoesNotPublishPartialResults()
    {
        using var cancellation = new CancellationTokenSource();
        var inspector = new StubInspector(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        MigrationWorkflow workflow = CreateWorkflow(inspector);
        MigrationWorkflowSession session = workflow.SelectRoots(
            workflow.CreateSession(), "source", "destination");

        session = await workflow.InspectAsync(session, cancellation.Token);

        Assert.Equal(MigrationWorkflowState.Cancelled, session.State);
        Assert.Null(session.SourceInspection);
        Assert.Null(session.DestinationInspection);
    }

    [Fact]
    public async Task ReconfiguringPlanCopiesInputsAndClearsDownstreamEvidence()
    {
        MigrationWorkflow workflow = CreateWorkflow(
            backupPlanner: new StubBackupPlanner(RequiredBackupPlan()),
            backupExecutor: new StubBackupExecutor([],
                new BackupExecutionResult(BackupExecutionStatus.Completed)),
            previewer: new StubPreviewer(new MigrationPreview(
                MigrationPlanStatus.Ready, [], [], [])));

        MigrationWorkflowSession session = await ReadyForBackup(workflow);
        session = workflow.PrepareBackup(session, "backups", "journals");
        session = await workflow.ExecuteBackupAsync(session, TestContext.Current.CancellationToken);

        var selected = new List<string> { "options.txt" };
        var decisions = new Dictionary<string, DestinationConflictDecision>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["config"] = DestinationConflictDecision.Replace,
        };
        session = workflow.ConfigurePlan(session, selected, decisions);

        selected.Add("saves");
        decisions["config"] = DestinationConflictDecision.Skip;

        Assert.Equal(MigrationWorkflowState.Preview, session.State);
        Assert.Equal(["options.txt"], session.SelectedEntryNames);
        Assert.Equal(DestinationConflictDecision.Replace, session.ConflictDecisions["config"]);
        Assert.Null(session.MigrationPreview);
        Assert.Null(session.BackupPlan);
        Assert.Null(session.BackupResult);
        Assert.Null(session.BackupParent);
        Assert.Null(session.JournalParent);
    }

    [Fact]
    public async Task ExecutionCancellationExceptionRequiresRecovery()
    {
        using var cancellation = new CancellationTokenSource();
        MigrationWorkflow workflow = CreateWorkflow(
            backupPlanner: new StubBackupPlanner(RequiredBackupPlan()),
            backupExecutor: new StubBackupExecutor([],
                new BackupExecutionResult(BackupExecutionStatus.Completed)),
            execution: new ThrowingExecutionOrchestrator());

        MigrationWorkflowSession session = await ReadyForBackup(workflow);
        session = workflow.PrepareBackup(session, "backups", "journals");
        session = await workflow.ExecuteBackupAsync(session, cancellation.Token);
        session = workflow.PrepareExecution(session);
        cancellation.Cancel();
        session = await workflow.ExecuteAsync(session, cancellation.Token);

        Assert.Equal(MigrationWorkflowState.RecoveryRequired, session.State);
        Assert.Equal(MigrationWorkflowFailureKind.ExecutionRecoveryRequired, session.FailureKind);
    }

    [Fact]
    public void SessionConstructionIsApplicationOwned()
    {
        var constructor = typeof(MigrationWorkflowSession).GetConstructor(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
            binder: null,
            [typeof(MigrationWorkflowState)],
            modifiers: null);

        Assert.Null(constructor);
        Assert.All(
            typeof(MigrationWorkflowSession).GetProperties(),
            property => Assert.False(property.SetMethod?.IsPublic ?? false));
    }

    [Fact]
    public async Task BackupIsBlockedUntilCapacityIsReadyForTheSameWorkspace()
    {
        MigrationWorkflow workflow = CreateWorkflow();
        MigrationWorkflowSession session = await InspectedSession(workflow);
        session = workflow.ConfigurePlan(session, ["options.txt"]);
        session = workflow.CreatePreview(session);

        Assert.False(session.CanPrepareBackup);
        Assert.Equal(MigrationWorkflowFailureKind.InvalidState,
            workflow.PrepareBackup(session, null, "journals").FailureKind);

        session = await workflow.EvaluateCapacityAsync(
            session, "journals", TestContext.Current.CancellationToken);
        Assert.True(session.CanPrepareBackup);
        Assert.Null(workflow.PrepareBackup(session, null, "journals").FailureKind);
        Assert.Equal(MigrationWorkflowFailureKind.InvalidState,
            workflow.PrepareBackup(session, null, "other-journals").FailureKind);
    }

    [Fact]
    public async Task WorkspaceAndPlanChangesInvalidateCapacityEvidence()
    {
        MigrationWorkflow workflow = CreateWorkflow();
        MigrationWorkflowSession session = await ReadyForBackup(workflow);

        session = workflow.InvalidateCapacity(session);
        Assert.Null(session.CapacityEstimate);
        Assert.Null(session.CapacityWorkspaceRoot);
        Assert.False(session.CanPrepareBackup);

        session = await workflow.EvaluateCapacityAsync(
            session, "journals", TestContext.Current.CancellationToken);
        session = workflow.ConfigurePlan(session, ["options.txt"]);
        Assert.Null(session.CapacityEstimate);
        Assert.Null(session.CapacityWorkspaceRoot);
    }

    [Fact]
    public async Task UnavailableCapacityDoesNotDestroyReadOnlyPreview()
    {
        MigrationWorkflow workflow = CreateWorkflow(
            capacity: new StubCapacityPreflight(new MigrationCapacityEstimate(
                MigrationCapacityStatus.Unavailable,
                MigrationCapacityFailureKind.VolumeUnavailable)));
        MigrationWorkflowSession session = await InspectedSession(workflow);
        session = workflow.ConfigurePlan(session, ["options.txt"]);
        session = workflow.CreatePreview(session);
        MigrationPreview? preview = session.MigrationPreview;

        session = await workflow.EvaluateCapacityAsync(
            session, "journals", TestContext.Current.CancellationToken);

        Assert.Same(preview, session.MigrationPreview);
        Assert.Equal(MigrationWorkflowState.ReadyForBackup, session.State);
        Assert.Equal(MigrationWorkflowFailureKind.CapacityUnavailable, session.FailureKind);
        Assert.False(session.CanPrepareBackup);
    }

    [Fact]
    public async Task MergeDecisionIsInspectedReadOnlyThenPlannedReadyWithItsEvidence()
    {
        var inspector = new StubMergeInspector(name => new MergeInspectionResult(
            name, MergeUnit.File, MergeInspectionStatus.Clean, 2, 1, 3, 100));
        MigrationWorkflow workflow = MergeWorkflow(inspector);

        MigrationWorkflowSession session = await MergeConfiguredSession(workflow);
        Assert.Equal(1, session.MigrationPlan!.MergeInspectionRequiredCount);

        session = await workflow.InspectMergesAsync(session, TestContext.Current.CancellationToken);
        session = workflow.CreatePreview(session);

        Assert.Equal(["config"], inspector.Inspected);
        Assert.Null(session.FailureKind);
        Assert.Equal(MigrationWorkflowState.ReadyForBackup, session.State);
        MigrationPlanEntry entry = Assert.Single(session.MigrationPlan!.Entries, item => item.Name == "config");
        Assert.Equal(MigrationPlanDisposition.ReadyToMerge, entry.Disposition);
        Assert.Same(session.MergeInspections["config"], entry.MergeInspection);
        Assert.Equal(1, session.MigrationPreview!.MergeCount);
    }

    [Fact]
    public async Task InspectionFailureOrMissingInspectorBlocksMergeButKeepsTheDecisionEditable()
    {
        MigrationWorkflow failing = MergeWorkflow(new StubMergeInspector(_ => throw new IOException("private path")));
        MigrationWorkflow missing = MergeWorkflow(mergeInspector: null);

        foreach (MigrationWorkflow workflow in new[] { failing, missing })
        {
            MigrationWorkflowSession session = await MergeConfiguredSession(workflow);
            session = await workflow.InspectMergesAsync(session, TestContext.Current.CancellationToken);

            MigrationPlanEntry entry = Assert.Single(session.MigrationPlan!.Entries, item => item.Name == "config");
            Assert.Equal(MigrationPlanDisposition.BlockedMergeConflict, entry.Disposition);
            Assert.Equal(MergeInspectionStatus.Unavailable, entry.MergeInspection!.Status);
            Assert.Equal(MigrationPlanStatus.Blocked, session.MigrationPlan.Status);
        }
    }

    [Fact]
    public async Task CancelledMergeInspectionKeepsThePlanWaitingAndReportsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        MigrationWorkflow workflow = MergeWorkflow(new StubMergeInspector(_ =>
        {
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("unreachable");
        }));

        MigrationWorkflowSession session = await MergeConfiguredSession(workflow);
        MigrationWorkflowSession after = await workflow.InspectMergesAsync(session, cancellation.Token);

        Assert.Equal(MigrationWorkflowFailureKind.Cancelled, after.FailureKind);
        Assert.Same(session.MigrationPlan, after.MigrationPlan);
        Assert.Empty(after.MergeInspections);
    }

    [Fact]
    public async Task ReconfiguringChoicesDiscardsMergeEvidence()
    {
        MigrationWorkflow workflow = MergeWorkflow(new StubMergeInspector(name => new MergeInspectionResult(
            name, MergeUnit.File, MergeInspectionStatus.Clean, 0, 0, 0, 0)));
        MigrationWorkflowSession session = await MergeConfiguredSession(workflow);
        session = await workflow.InspectMergesAsync(session, TestContext.Current.CancellationToken);
        Assert.NotEmpty(session.MergeInspections);

        session = workflow.ConfigurePlan(
            session,
            ["config"],
            new Dictionary<string, DestinationConflictDecision> { ["config"] = DestinationConflictDecision.Merge });

        Assert.Empty(session.MergeInspections);
        Assert.Equal(1, session.MigrationPlan!.MergeInspectionRequiredCount);
    }

    private static MigrationWorkflow MergeWorkflow(IMergeInspector? mergeInspector) =>
        CreateWorkflow(
            inspector: new StubInspector(_ => new InstanceInspectionResult(
                EntryState.Directory,
                [new EntryObservation("config", ExpectedEntryKind.Directory, EntryState.Directory)])),
            planner: new MigrationPlanner(),
            previewer: new MigrationPreviewer(),
            mergeInspector: mergeInspector);

    private static async Task<MigrationWorkflowSession> MergeConfiguredSession(MigrationWorkflow workflow)
    {
        MigrationWorkflowSession session = await InspectedSession(workflow);
        return workflow.ConfigurePlan(
            session,
            ["config"],
            new Dictionary<string, DestinationConflictDecision> { ["config"] = DestinationConflictDecision.Merge });
    }

    private sealed class StubMergeInspector(Func<string, MergeInspectionResult> inspect) : IMergeInspector
    {
        public List<string> Inspected { get; } = [];

        public Task<MergeInspectionResult> InspectAsync(
            string sourceRoot,
            string destinationRoot,
            string entryName,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("source", sourceRoot);
            Assert.Equal("destination", destinationRoot);
            Inspected.Add(entryName);
            return Task.FromResult(inspect(entryName));
        }
    }

    private static async Task<MigrationWorkflowSession> ReadyForBackup(MigrationWorkflow workflow)
    {
        MigrationWorkflowSession session = await InspectedSession(workflow);
        session = workflow.ConfigurePlan(session, ["options.txt"], null);
        session = workflow.CreatePreview(session);
        return await workflow.EvaluateCapacityAsync(
            session,
            "journals",
            TestContext.Current.CancellationToken);
    }

    private static async Task<MigrationWorkflowSession> InspectedSession(MigrationWorkflow workflow)
    {
        MigrationWorkflowSession session = workflow.SelectRoots(
            workflow.CreateSession(), "source", "destination");
        return await workflow.InspectAsync(session, TestContext.Current.CancellationToken);
    }

    private static MigrationWorkflow CreateWorkflow(
        IInstanceInspector? inspector = null,
        IMigrationPlanner? planner = null,
        IMigrationPreviewer? previewer = null,
        IMigrationCapacityPreflight? capacity = null,
        IBackupPlanner? backupPlanner = null,
        IBackupExecutor? backupExecutor = null,
        IExecutionOrchestrator? execution = null,
        IExecutionWorkspaceSafetyValidator? workspace = null,
        IMergeInspector? mergeInspector = null) =>
        new(
            inspector ?? new StubInspector(_ => Inspection()),
            planner ?? new StubPlanner(ReadyPlan()),
            previewer ?? new StubPreviewer(new MigrationPreview(
                MigrationPlanStatus.Ready, [], [], [])),
            capacity ?? new StubCapacityPreflight(),
            backupPlanner ?? new StubBackupPlanner(),
            backupExecutor ?? new StubBackupExecutor([]),
            execution ?? new StubExecutionOrchestrator([]),
            workspace ?? new StubWorkspaceValidator(),
            mergeInspector);

    private sealed class StubWorkspaceValidator(
        ExecutionWorkspaceSafetyStatus status = ExecutionWorkspaceSafetyStatus.Safe,
        bool throws = false,
        List<string>? calls = null) : IExecutionWorkspaceSafetyValidator
    {
        public Task<ExecutionWorkspaceSafetyResult> ValidateAsync(string sourceRoot,
            string destinationRoot, string journalParent, CancellationToken cancellationToken = default)
        {
            Assert.Equal("source", sourceRoot);
            Assert.Equal("destination", destinationRoot);
            calls?.Add("workspace:" + journalParent);
            if (throws)
            {
                throw new IOException("Synthetic private details must not be exposed.");
            }
            return Task.FromResult(new ExecutionWorkspaceSafetyResult(status));
        }
    }

    private sealed class StubCapacityPreflight(
        MigrationCapacityEstimate? estimate = null) : IMigrationCapacityPreflight
    {
        public Task<MigrationCapacityEstimate> EvaluateAsync(
            MigrationCapacityRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(estimate ?? new MigrationCapacityEstimate(
                MigrationCapacityStatus.Ready,
                MigrationCapacityFailureKind.None,
                DestinationRequiredBytes: 1,
                SafetyWorkspaceRequiredBytes: 1,
                DestinationAvailableBytes: 2,
                SafetyWorkspaceAvailableBytes: 2));
        }
    }

    private static InstanceInspectionResult Inspection() =>
        new(
            EntryState.Directory,
            [new EntryObservation(
                "options.txt",
                ExpectedEntryKind.File,
                EntryState.File)]);

    private static MigrationPlan ReadyPlan() =>
        new(
            EntryState.Directory,
            EntryState.Directory,
            [new MigrationPlanEntry(
                "options.txt",
                ExpectedEntryKind.File,
                EntryState.File,
                EntryState.Missing,
                true,
                MigrationPlanDisposition.ReadyToCopy)],
            []);

    private sealed class StubInspector(Func<string, InstanceInspectionResult> inspect) : IInstanceInspector
    {
        public Task<InstanceInspectionResult> InspectAsync(string candidatePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(inspect(candidatePath));
        }
    }

    private sealed class StubPlanner(MigrationPlan plan) : IMigrationPlanner
    {
        public MigrationPlan CreatePlan(InstanceInspectionResult source, InstanceInspectionResult destination,
            IEnumerable<string> selectedEntryNames, IReadOnlyDictionary<string, DestinationConflictDecision>? conflictDecisions = null,
            IReadOnlyDictionary<string, MergeInspectionResult>? mergeInspections = null) => plan;

        public MigrationPlan CreateRecommendedPlan(InstanceInspectionResult source, InstanceInspectionResult destination,
            IReadOnlyDictionary<string, DestinationConflictDecision>? conflictDecisions = null) => plan;
    }

    private sealed class StubPreviewer(MigrationPreview preview) : IMigrationPreviewer
    {
        public MigrationPreview CreatePreview(MigrationPlan plan) => preview;
    }

    private sealed class StubBackupPlanner(BackupPlan? backupPlan = null) : IBackupPlanner
    {
        public BackupPlan CreateBackupPlan(MigrationPlan migrationPlan) =>
            backupPlan ?? new(BackupPlanStatus.NotRequired, [], []);

        public BackupManifestDraft CreateManifestDraft(BackupPlan backupPlan) =>
            throw new NotSupportedException();
    }

    private sealed class StubBackupExecutor(
        List<string> calls,
        BackupExecutionResult? result = null) : IBackupExecutor
    {
        public Task<BackupExecutionResult> ExecuteAsync(string destinationRoot, string backupParent,
            BackupPlan plan, CancellationToken cancellationToken = default)
        {
            calls.Add("backup");
            return Task.FromResult(result ?? new BackupExecutionResult(BackupExecutionStatus.NotRequired));
        }
    }

    private sealed class ThrowingExecutionOrchestrator : IExecutionOrchestrator
    {
        public Task<ExecutionOrchestrationResult> ExecuteAsync(
            ExecutionOrchestrationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ExecutionOrchestrationResult>(
                new OperationCanceledException(cancellationToken));
    }

    private static BackupPlan RequiredBackupPlan() =>
        new(
            BackupPlanStatus.Ready,
            [new BackupPlanEntry(
                "config",
                ExpectedEntryKind.Directory,
                EntryState.Directory)],
            []);

    private sealed class StubExecutionOrchestrator(List<string> calls,
        ExecutionOrchestrationStatus status = ExecutionOrchestrationStatus.Completed) : IExecutionOrchestrator
    {
        public Task<ExecutionOrchestrationResult> ExecuteAsync(ExecutionOrchestrationRequest request,
            CancellationToken cancellationToken = default)
        {
            calls.Add("execute");
            return Task.FromResult(new ExecutionOrchestrationResult(status));
        }
    }
}
