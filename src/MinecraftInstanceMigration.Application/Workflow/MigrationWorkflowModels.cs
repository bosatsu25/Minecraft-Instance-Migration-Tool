using System.Collections.ObjectModel;
using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Workflow;

public enum MigrationWorkflowState
{
    SelectRoots,
    Inspect,
    ConfigurePlan,
    Preview,
    ReadyForBackup,
    BackupReady,
    ReadyForExecution,
    Executing,
    Completed,
    RecoveryRequired,
    Blocked,
    Cancelled,
}

public enum MigrationWorkflowFailureKind
{
    InvalidState,
    InvalidRoots,
    InspectionFailed,
    PlanFailed,
    PlanNotReady,
    BackupRequired,
    JournalRequired,
    BackupNotReady,
    BackupFailed,
    ExecutionFailed,
    ExecutionRecoveryRequired,
    Cancelled,
}

public sealed record MigrationWorkflowSession
{
    public MigrationWorkflowSession(MigrationWorkflowState state)
    {
        State = state;
        SelectedEntryNames = [];
        ConflictDecisions = new ReadOnlyDictionary<string, DestinationConflictDecision>(
            new Dictionary<string, DestinationConflictDecision>(
                StringComparer.OrdinalIgnoreCase));
    }

    public MigrationWorkflowState State { get; init; }

    public string? SourceRoot { get; init; }

    public string? DestinationRoot { get; init; }

    public string? BackupParent { get; init; }

    public string? JournalParent { get; init; }

    public InstanceInspectionResult? SourceInspection { get; init; }

    public InstanceInspectionResult? DestinationInspection { get; init; }

    public IReadOnlyList<string> SelectedEntryNames { get; init; }

    public IReadOnlyDictionary<string, DestinationConflictDecision> ConflictDecisions { get; init; }

    public MigrationPlan? MigrationPlan { get; init; }

    public MigrationPreview? MigrationPreview { get; init; }

    public BackupPlan? BackupPlan { get; init; }

    public BackupExecutionResult? BackupResult { get; init; }

    public ExecutionOrchestrationResult? ExecutionResult { get; init; }

    public MigrationWorkflowFailureKind? FailureKind { get; init; }

    public bool HasInspections =>
        SourceInspection is not null && DestinationInspection is not null;

    public bool CanPrepareBackup =>
        State == MigrationWorkflowState.ReadyForBackup &&
        MigrationPreview?.Status == MigrationPlanStatus.Ready;

    public bool CanExecute =>
        State == MigrationWorkflowState.ReadyForExecution &&
        MigrationPlan is not null &&
        BackupResult?.IsComplete == true;
}
