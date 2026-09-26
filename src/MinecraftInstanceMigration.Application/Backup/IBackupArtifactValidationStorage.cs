using MinecraftInstanceMigration.Domain.Backup;

namespace MinecraftInstanceMigration.Application.Backup;

public interface IBackupArtifactValidationStorage
{
    Task<BackupArtifactValidationResult> ValidateBackupAsync(
        string backupRoot,
        BackupPlan plan,
        CancellationToken cancellationToken);
}
