using MinecraftInstanceMigration.Domain.Backup;

namespace MinecraftInstanceMigration.Application.Backup;

public interface IBackupStorage
{
    Task<BackupExecutionResult> CreateBackupAsync(
        string destinationRoot,
        string backupParent,
        BackupPlan plan,
        BackupManifestDraft manifest,
        CancellationToken cancellationToken);
}
