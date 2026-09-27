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
        IExecutionOrchestrator? execution = null) =>
        new(
            inspector ?? new StubInspector(_ => Inspection()),
            planner ?? new StubPlanner(ReadyPlan()),
            previewer ?? new StubPreviewer(new MigrationPreview(
                MigrationPlanStatus.Ready, [], [], [])),
            capacity ?? new StubCapacityPreflight(),
            backupPlanner ?? new StubBackupPlanner(),
            backupExecutor ?? new StubBackupExecutor([]),
            execution ?? new StubExecutionOrchestrator([]));

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
            IEnumerable<string> selectedEntryNames, IReadOnlyDictionary<string, DestinationConflictDecision>? conflictDecisions = null) => plan;

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
