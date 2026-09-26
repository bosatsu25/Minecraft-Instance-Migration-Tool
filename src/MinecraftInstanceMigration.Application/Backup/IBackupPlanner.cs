using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Backup;

public interface IBackupPlanner
{
    BackupPlan CreateBackupPlan(MigrationPlan migrationPlan);

    BackupManifestDraft CreateManifestDraft(BackupPlan backupPlan);
}
