using System.Collections.ObjectModel;
using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Application.Capacity;
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
    CapacityUnavailable,
    CapacityInsufficient,
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
    internal MigrationWorkflowSession(MigrationWorkflowState state)
    {
        State = state;
        SelectedEntryNames = [];
        ConflictDecisions = new ReadOnlyDictionary<string, DestinationConflictDecision>(
            new Dictionary<string, DestinationConflictDecision>(
                StringComparer.OrdinalIgnoreCase));
    }

    public MigrationWorkflowState State { get; internal init; }

    public string? SourceRoot { get; internal init; }

    public string? DestinationRoot { get; internal init; }

    public string? BackupParent { get; internal init; }

    public string? JournalParent { get; internal init; }

    public InstanceInspectionResult? SourceInspection { get; internal init; }

    public InstanceInspectionResult? DestinationInspection { get; internal init; }

    public IReadOnlyList<string> SelectedEntryNames { get; internal init; }

    public IReadOnlyDictionary<string, DestinationConflictDecision> ConflictDecisions { get; internal init; }

    public MigrationPlan? MigrationPlan { get; internal init; }

    public MigrationPreview? MigrationPreview { get; internal init; }

    public MigrationCapacityEstimate? CapacityEstimate { get; internal init; }

    public string? CapacityWorkspaceRoot { get; internal init; }

    public BackupPlan? BackupPlan { get; internal init; }

    public BackupExecutionResult? BackupResult { get; internal init; }

    public ExecutionOrchestrationResult? ExecutionResult { get; internal init; }

    public MigrationWorkflowFailureKind? FailureKind { get; internal init; }

    public bool HasInspections =>
        SourceInspection is not null && DestinationInspection is not null;

    public bool CanPrepareBackup =>
        State == MigrationWorkflowState.ReadyForBackup &&
        MigrationPreview?.Status == MigrationPlanStatus.Ready &&
        CapacityEstimate?.IsReady == true;

    public bool CanExecute =>
        State == MigrationWorkflowState.ReadyForExecution &&
        MigrationPlan is not null &&
        BackupResult?.IsComplete == true;
}
