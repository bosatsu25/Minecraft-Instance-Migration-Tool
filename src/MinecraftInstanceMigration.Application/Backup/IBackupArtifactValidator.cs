using MinecraftInstanceMigration.Domain.Backup;

namespace MinecraftInstanceMigration.Application.Backup;

public interface IBackupArtifactValidator
{
    Task<BackupArtifactValidationResult> ValidateAsync(
        string backupRoot,
        BackupPlan plan,
        CancellationToken cancellationToken = default);
}
