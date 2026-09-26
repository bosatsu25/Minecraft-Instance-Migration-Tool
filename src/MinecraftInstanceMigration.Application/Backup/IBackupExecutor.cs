using MinecraftInstanceMigration.Domain.Backup;

namespace MinecraftInstanceMigration.Application.Backup;

public interface IBackupExecutor
{
    Task<BackupExecutionResult> ExecuteAsync(
        string destinationRoot,
        string backupParent,
        BackupPlan plan,
        CancellationToken cancellationToken = default);
}
