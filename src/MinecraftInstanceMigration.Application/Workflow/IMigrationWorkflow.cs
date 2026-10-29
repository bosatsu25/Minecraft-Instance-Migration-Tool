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

    // Compares source and destination for every entry whose Merge decision still lacks evidence,
    // then replans with that evidence. No files are changed.
    Task<MigrationWorkflowSession> InspectMergesAsync(
        MigrationWorkflowSession session,
        CancellationToken cancellationToken = default);

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
