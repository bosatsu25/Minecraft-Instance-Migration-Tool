using MinecraftInstanceMigration.Domain.Backup;

namespace MinecraftInstanceMigration.Application.Backup;

public sealed class BackupArtifactValidator(IBackupArtifactValidationStorage storage) : IBackupArtifactValidator
{
    private readonly IBackupArtifactValidationStorage storage =
        storage ?? throw new ArgumentNullException(nameof(storage));

    public async Task<BackupArtifactValidationResult> ValidateAsync(
        string backupRoot,
        BackupPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.CanStartBackup)
        {
            return new BackupArtifactValidationResult(
                BackupArtifactValidationStatus.Invalid,
                BackupArtifactFailureKind.InvalidPlan);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new BackupArtifactValidationResult(BackupArtifactValidationStatus.Cancelled);
        }

        return await storage.ValidateBackupAsync(backupRoot, plan, cancellationToken);
    }
}
