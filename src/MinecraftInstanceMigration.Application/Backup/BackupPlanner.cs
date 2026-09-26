using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Backup;

public sealed class BackupPlanner : IBackupPlanner
{
    public BackupPlan CreateBackupPlan(MigrationPlan migrationPlan) =>
        BackupPlanPolicy.Create(migrationPlan);

    public BackupManifestDraft CreateManifestDraft(BackupPlan backupPlan) =>
        BackupPlanPolicy.CreateManifestDraft(backupPlan);
}
