using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Execution;

public sealed class ExecutionSafetyPlanner : IExecutionSafetyPlanner
{
    public ExecutionJournalDraft CreateJournalDraft(MigrationPlan migrationPlan) =>
        ExecutionJournalPolicy.Create(migrationPlan);

    public RollbackPlan CreateRollbackPlan(
        ExecutionJournalDraft draft,
        ExecutionJournalSnapshot snapshot,
        BackupArtifactValidationResult? backupValidation = null) =>
        RollbackPlanPolicy.Create(
            draft,
            snapshot,
            backupValidation?.IsValid == true);
}
