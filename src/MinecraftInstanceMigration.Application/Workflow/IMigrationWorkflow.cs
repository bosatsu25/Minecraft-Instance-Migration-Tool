using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Application.Capacity;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Application.Inspection;
using MinecraftInstanceMigration.Application.Planning;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Workflow;

public interface IMigrationWorkflow
{
    MigrationWorkflowSession CreateSession();

    MigrationWorkflowSession SelectRoots(
        MigrationWorkflowSession session,
        string sourceRoot,
        string destinationRoot);

    Task<MigrationWorkflowSession> InspectAsync(
        MigrationWorkflowSession session,
        CancellationToken cancellationToken = default);

    MigrationWorkflowSession ConfigurePlan(
        MigrationWorkflowSession session,
        IEnumerable<string> selectedEntryNames,
        IReadOnlyDictionary<string, DestinationConflictDecision>? conflictDecisions = null);

    MigrationWorkflowSession CreatePreview(MigrationWorkflowSession session);

    Task<MigrationWorkflowSession> EvaluateCapacityAsync(
        MigrationWorkflowSession session,
        string safetyWorkspaceRoot,
        CancellationToken cancellationToken = default);

    MigrationWorkflowSession InvalidateCapacity(MigrationWorkflowSession session);

    MigrationWorkflowSession PrepareBackup(
        MigrationWorkflowSession session,
        string? backupParent,
        string? journalParent);

    Task<MigrationWorkflowSession> ExecuteBackupAsync(
        MigrationWorkflowSession session,
        CancellationToken cancellationToken = default);

    MigrationWorkflowSession PrepareExecution(MigrationWorkflowSession session);

    Task<MigrationWorkflowSession> ExecuteAsync(
        MigrationWorkflowSession session,
        CancellationToken cancellationToken = default);
}
