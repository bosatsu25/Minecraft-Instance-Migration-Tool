using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Execution;

public interface IExecutionSafetyPlanner
{
    ExecutionJournalDraft CreateJournalDraft(MigrationPlan migrationPlan);

    RollbackPlan CreateRollbackPlan(
        ExecutionJournalDraft draft,
        ExecutionJournalSnapshot snapshot,
        BackupArtifactValidationResult? backupValidation = null);
}
