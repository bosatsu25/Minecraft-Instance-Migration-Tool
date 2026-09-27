using MinecraftInstanceMigration.Application.Backup;
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
    public void PreviewThatNeedsDecisionDoesNotAdvanceToBackup()
    {
        var preview = new MigrationPreview(
            MigrationPlanStatus.NeedsDecision,
            [],
            [],
            [new ConflictDecisionIssue("config", ConflictDecisionIssueKind.NoDestinationConflict)]);
        MigrationWorkflow workflow = CreateWorkflow(
            planner: new StubPlanner(ReadyPlan()),
            previewer: new StubPreviewer(preview));

        MigrationWorkflowSession session = workflow.SelectRoots(
            workflow.CreateSession(), "source", "destination");
        session = session with
        {
            State = MigrationWorkflowState.ConfigurePlan,
            SourceInspection = Inspection(),
            DestinationInspection = Inspection(),
        };
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
            backupPlanner: new StubBackupPlanner(),
            backupExecutor: new StubBackupExecutor(calls),
            execution: new StubExecutionOrchestrator(calls));

        MigrationWorkflowSession session = ReadyForBackup(workflow);
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
    public async Task RecoveryRequiredExecutionStopsInRecoveryState()
    {
        MigrationWorkflow workflow = CreateWorkflow(
            planner: new StubPlanner(ReadyPlan()),
            previewer: new StubPreviewer(new MigrationPreview(
                MigrationPlanStatus.Ready, [], [], [])),
            backupPlanner: new StubBackupPlanner(),
            backupExecutor: new StubBackupExecutor([]),
            execution: new StubExecutionOrchestrator([], ExecutionOrchestrationStatus.RecoveryRequired));

        MigrationWorkflowSession session = ReadyForBackup(workflow);
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

    private static MigrationWorkflowSession ReadyForBackup(MigrationWorkflow workflow)
    {
        MigrationWorkflowSession session = workflow.SelectRoots(
            workflow.CreateSession(), "source", "destination") with
        {
            State = MigrationWorkflowState.ConfigurePlan,
            SourceInspection = Inspection(),
            DestinationInspection = Inspection(),
        };
        session = workflow.ConfigurePlan(session, ["options.txt"], null);
        return workflow.CreatePreview(session);
    }

    private static MigrationWorkflow CreateWorkflow(
        IInstanceInspector? inspector = null,
        IMigrationPlanner? planner = null,
        IMigrationPreviewer? previewer = null,
        IBackupPlanner? backupPlanner = null,
        IBackupExecutor? backupExecutor = null,
        IExecutionOrchestrator? execution = null) =>
        new(
            inspector ?? new StubInspector(_ => Inspection()),
            planner ?? new StubPlanner(ReadyPlan()),
            previewer ?? new StubPreviewer(new MigrationPreview(
                MigrationPlanStatus.Ready, [], [], [])),
            backupPlanner ?? new StubBackupPlanner(),
            backupExecutor ?? new StubBackupExecutor([]),
            execution ?? new StubExecutionOrchestrator([]));

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

    private sealed class StubBackupPlanner : IBackupPlanner
    {
        public BackupPlan CreateBackupPlan(MigrationPlan migrationPlan) =>
            new(BackupPlanStatus.NotRequired, [], []);

        public BackupManifestDraft CreateManifestDraft(BackupPlan backupPlan) =>
            throw new NotSupportedException();
    }

    private sealed class StubBackupExecutor(List<string> calls) : IBackupExecutor
    {
        public Task<BackupExecutionResult> ExecuteAsync(string destinationRoot, string backupParent,
            BackupPlan plan, CancellationToken cancellationToken = default)
        {
            calls.Add("backup");
            return Task.FromResult(new BackupExecutionResult(BackupExecutionStatus.NotRequired));
        }
    }

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
