using MinecraftInstanceMigration.Domain.Backup;

namespace MinecraftInstanceMigration.Application.Backup;

public sealed class BackupExecutor(IBackupStorage storage, IBackupPlanner planner) : IBackupExecutor
{
    private readonly IBackupStorage storage = storage ?? throw new ArgumentNullException(nameof(storage));
    private readonly IBackupPlanner planner = planner ?? throw new ArgumentNullException(nameof(planner));

    public async Task<BackupExecutionResult> ExecuteAsync(
        string destinationRoot,
        string backupParent,
        BackupPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Status == BackupPlanStatus.NotRequired)
        {
            return new BackupExecutionResult(BackupExecutionStatus.NotRequired);
        }

        if (!plan.CanStartBackup)
        {
            return new BackupExecutionResult(
                BackupExecutionStatus.Failed,
                FailureKind: BackupFailureKind.InvalidPlan);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new BackupExecutionResult(BackupExecutionStatus.Cancelled);
        }

        BackupManifestDraft manifest = planner.CreateManifestDraft(plan);
        return await storage.CreateBackupAsync(
            destinationRoot,
            backupParent,
            plan,
            manifest,
            cancellationToken);
    }
}
