using System.Collections.ObjectModel;
using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Application.Inspection;
using MinecraftInstanceMigration.Application.Planning;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Workflow;

public sealed class MigrationWorkflow(
    IInstanceInspector inspector,
    IMigrationPlanner planner,
    IMigrationPreviewer previewer,
    IBackupPlanner backupPlanner,
    IBackupExecutor backupExecutor,
    IExecutionOrchestrator executionOrchestrator) : IMigrationWorkflow
{
    private readonly IInstanceInspector inspector =
        inspector ?? throw new ArgumentNullException(nameof(inspector));
    private readonly IMigrationPlanner planner =
        planner ?? throw new ArgumentNullException(nameof(planner));
    private readonly IMigrationPreviewer previewer =
        previewer ?? throw new ArgumentNullException(nameof(previewer));
    private readonly IBackupPlanner backupPlanner =
        backupPlanner ?? throw new ArgumentNullException(nameof(backupPlanner));
    private readonly IBackupExecutor backupExecutor =
        backupExecutor ?? throw new ArgumentNullException(nameof(backupExecutor));
    private readonly IExecutionOrchestrator executionOrchestrator =
        executionOrchestrator ?? throw new ArgumentNullException(nameof(executionOrchestrator));

    public MigrationWorkflowSession CreateSession() =>
        new(MigrationWorkflowState.SelectRoots);

    public MigrationWorkflowSession SelectRoots(
        MigrationWorkflowSession session,
        string sourceRoot,
        string destinationRoot)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.State is not MigrationWorkflowState.SelectRoots and
            not MigrationWorkflowState.Blocked and
            not MigrationWorkflowState.Cancelled and
            not MigrationWorkflowState.Completed and
            not MigrationWorkflowState.RecoveryRequired)
        {
            return Failure(session, MigrationWorkflowFailureKind.InvalidState);
        }

        if (string.IsNullOrWhiteSpace(sourceRoot) ||
            string.IsNullOrWhiteSpace(destinationRoot))
        {
            return Failure(session, MigrationWorkflowFailureKind.InvalidRoots);
        }

        return new MigrationWorkflowSession(MigrationWorkflowState.Inspect)
        {
            SourceRoot = sourceRoot,
            DestinationRoot = destinationRoot,
        };
    }

    public async Task<MigrationWorkflowSession> InspectAsync(
        MigrationWorkflowSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.State != MigrationWorkflowState.Inspect ||
            session.SourceRoot is null ||
            session.DestinationRoot is null)
        {
            return Failure(session, MigrationWorkflowFailureKind.InvalidState);
        }

        try
        {
            var source = await inspector.InspectAsync(
                session.SourceRoot,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var destination = await inspector.InspectAsync(
                session.DestinationRoot,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            return session with
            {
                State = MigrationWorkflowState.ConfigurePlan,
                SourceInspection = source,
                DestinationInspection = destination,
                FailureKind = null,
            };
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return session with
            {
                State = MigrationWorkflowState.Cancelled,
                SourceInspection = null,
                DestinationInspection = null,
                FailureKind = MigrationWorkflowFailureKind.Cancelled,
            };
        }
        catch (Exception)
        {
            return session with
            {
                State = MigrationWorkflowState.Blocked,
                SourceInspection = null,
                DestinationInspection = null,
                FailureKind = MigrationWorkflowFailureKind.InspectionFailed,
            };
        }
    }

    public MigrationWorkflowSession ConfigurePlan(
        MigrationWorkflowSession session,
        IEnumerable<string> selectedEntryNames,
        IReadOnlyDictionary<string, DestinationConflictDecision>? conflictDecisions = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(selectedEntryNames);

        if (session.State is not MigrationWorkflowState.ConfigurePlan and
            not MigrationWorkflowState.Preview and
            not MigrationWorkflowState.ReadyForBackup and
            not MigrationWorkflowState.BackupReady and
            not MigrationWorkflowState.ReadyForExecution ||
            session.SourceInspection is null ||
            session.DestinationInspection is null)
        {
            return Failure(session, MigrationWorkflowFailureKind.InvalidState);
        }

        try
        {
            string[] selected = selectedEntryNames.ToArray();
            Dictionary<string, DestinationConflictDecision> decisions =
                conflictDecisions is null
                    ? new Dictionary<string, DestinationConflictDecision>(
                        StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, DestinationConflictDecision>(
                        conflictDecisions,
                        StringComparer.OrdinalIgnoreCase);
            var plan = planner.CreatePlan(
                session.SourceInspection,
                session.DestinationInspection,
                selected,
                decisions);

            return session with
            {
                State = MigrationWorkflowState.Preview,
                SelectedEntryNames = Array.AsReadOnly(selected),
                ConflictDecisions = new ReadOnlyDictionary<string, DestinationConflictDecision>(decisions),
                MigrationPlan = plan,
                MigrationPreview = null,
                BackupPlan = null,
                BackupResult = null,
                ExecutionResult = null,
                BackupParent = null,
                JournalParent = null,
                FailureKind = null,
            };
        }
        catch (Exception)
        {
            return Failure(session, MigrationWorkflowFailureKind.PlanFailed);
        }
    }

    public MigrationWorkflowSession CreatePreview(MigrationWorkflowSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.State != MigrationWorkflowState.Preview ||
            session.MigrationPlan is null)
        {
            return Failure(session, MigrationWorkflowFailureKind.InvalidState);
        }

        try
        {
            var preview = previewer.CreatePreview(session.MigrationPlan);
            return preview.Status == MigrationPlanStatus.Ready
                ? session with
                {
                    State = MigrationWorkflowState.ReadyForBackup,
                    MigrationPreview = preview,
                    FailureKind = null,
                }
                : session with
                {
                    State = MigrationWorkflowState.Preview,
                    MigrationPreview = preview,
                    FailureKind = MigrationWorkflowFailureKind.PlanNotReady,
                };
        }
        catch (Exception)
        {
            return Failure(session, MigrationWorkflowFailureKind.PlanFailed);
        }
    }

    public MigrationWorkflowSession PrepareBackup(
        MigrationWorkflowSession session,
        string? backupParent,
        string? journalParent)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!session.CanPrepareBackup ||
            session.MigrationPlan is null)
        {
            return Failure(session, MigrationWorkflowFailureKind.InvalidState);
        }

        try
        {
            var backupPlan = backupPlanner.CreateBackupPlan(
                session.MigrationPlan);
            if (backupPlan.Status == BackupPlanStatus.Blocked)
            {
                return session with
                {
                    BackupPlan = backupPlan,
                    FailureKind = MigrationWorkflowFailureKind.BackupNotReady,
                };
            }

            if (backupPlan.RequiresBackup &&
                string.IsNullOrWhiteSpace(backupParent))
            {
                return session with
                {
                    BackupPlan = backupPlan,
                    FailureKind = MigrationWorkflowFailureKind.BackupRequired,
                };
            }

            return session with
            {
                BackupPlan = backupPlan,
                BackupParent = string.IsNullOrWhiteSpace(backupParent) ? null : backupParent,
                JournalParent = string.IsNullOrWhiteSpace(journalParent) ? null : journalParent,
                FailureKind = null,
            };
        }
        catch (Exception)
        {
            return Failure(session, MigrationWorkflowFailureKind.BackupNotReady);
        }
    }

    public async Task<MigrationWorkflowSession> ExecuteBackupAsync(
        MigrationWorkflowSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.State != MigrationWorkflowState.ReadyForBackup ||
            session.MigrationPlan is null ||
            session.BackupPlan is null ||
            session.SourceRoot is null ||
            session.DestinationRoot is null)
        {
            return Failure(session, MigrationWorkflowFailureKind.InvalidState);
        }

        if (session.BackupPlan.Status == BackupPlanStatus.Blocked)
        {
            return Failure(session, MigrationWorkflowFailureKind.BackupNotReady);
        }

        if (session.BackupPlan.Status == BackupPlanStatus.NotRequired)
        {
            return session with
            {
                State = MigrationWorkflowState.BackupReady,
                BackupResult = new BackupExecutionResult(BackupExecutionStatus.NotRequired),
                FailureKind = null,
            };
        }

        if (string.IsNullOrWhiteSpace(session.BackupParent))
        {
            return Failure(session, MigrationWorkflowFailureKind.BackupRequired);
        }

        try
        {
            var result = await backupExecutor.ExecuteAsync(
                session.DestinationRoot,
                session.BackupParent,
                session.BackupPlan,
                cancellationToken);

            return result.Status switch
            {
                BackupExecutionStatus.Completed or
                BackupExecutionStatus.NotRequired => session with
                {
                    State = MigrationWorkflowState.BackupReady,
                    BackupResult = result,
                    FailureKind = null,
                },
                BackupExecutionStatus.Cancelled => session with
                {
                    State = MigrationWorkflowState.Cancelled,
                    BackupResult = result,
                    FailureKind = MigrationWorkflowFailureKind.Cancelled,
                },
                _ => session with
                {
                    State = MigrationWorkflowState.ReadyForBackup,
                    BackupResult = result,
                    FailureKind = MigrationWorkflowFailureKind.BackupFailed,
                },
            };
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return session with
            {
                State = MigrationWorkflowState.Cancelled,
                FailureKind = MigrationWorkflowFailureKind.Cancelled,
            };
        }
        catch (Exception)
        {
            return Failure(session, MigrationWorkflowFailureKind.BackupFailed);
        }
    }

    public MigrationWorkflowSession PrepareExecution(MigrationWorkflowSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.State != MigrationWorkflowState.BackupReady ||
            session.BackupResult?.IsComplete != true)
        {
            return Failure(session, MigrationWorkflowFailureKind.InvalidState);
        }

        if (string.IsNullOrWhiteSpace(session.JournalParent))
        {
            return Failure(session, MigrationWorkflowFailureKind.JournalRequired);
        }

        return session with
        {
            State = MigrationWorkflowState.ReadyForExecution,
            FailureKind = null,
        };
    }

    public async Task<MigrationWorkflowSession> ExecuteAsync(
        MigrationWorkflowSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!session.CanExecute ||
            session.SourceRoot is null ||
            session.DestinationRoot is null ||
            session.JournalParent is null ||
            session.MigrationPlan is null)
        {
            return Failure(session, MigrationWorkflowFailureKind.InvalidState);
        }

        var executing = session with { State = MigrationWorkflowState.Executing };

        try
        {
            var result = await executionOrchestrator.ExecuteAsync(
                new ExecutionOrchestrationRequest(
                    session.SourceRoot,
                    session.DestinationRoot,
                    session.JournalParent,
                    session.BackupResult?.BackupRootPath,
                    session.MigrationPlan),
                cancellationToken);

            return result.Status switch
            {
                ExecutionOrchestrationStatus.Completed or
                ExecutionOrchestrationStatus.NotRequired => executing with
                {
                    State = MigrationWorkflowState.Completed,
                    ExecutionResult = result,
                    FailureKind = null,
                },
                ExecutionOrchestrationStatus.RecoveryRequired => executing with
                {
                    State = MigrationWorkflowState.RecoveryRequired,
                    ExecutionResult = result,
                    FailureKind = MigrationWorkflowFailureKind.ExecutionRecoveryRequired,
                },
                ExecutionOrchestrationStatus.Cancelled when result.AppliedSteps > 0 => executing with
                {
                    State = MigrationWorkflowState.RecoveryRequired,
                    ExecutionResult = result,
                    FailureKind = MigrationWorkflowFailureKind.ExecutionRecoveryRequired,
                },
                ExecutionOrchestrationStatus.Cancelled => executing with
                {
                    State = MigrationWorkflowState.Cancelled,
                    ExecutionResult = result,
                    FailureKind = MigrationWorkflowFailureKind.Cancelled,
                },
                _ => executing with
                {
                    State = MigrationWorkflowState.Blocked,
                    ExecutionResult = result,
                    FailureKind = MigrationWorkflowFailureKind.ExecutionFailed,
                },
            };
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return executing with
            {
                State = MigrationWorkflowState.RecoveryRequired,
                FailureKind = MigrationWorkflowFailureKind.ExecutionRecoveryRequired,
            };
        }
        catch (Exception)
        {
            return executing with
            {
                State = MigrationWorkflowState.RecoveryRequired,
                FailureKind = MigrationWorkflowFailureKind.ExecutionRecoveryRequired,
            };
        }
    }

    private static MigrationWorkflowSession Failure(
        MigrationWorkflowSession session,
        MigrationWorkflowFailureKind failureKind) =>
        session with { FailureKind = failureKind };
}
