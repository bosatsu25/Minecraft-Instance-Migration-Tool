using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Execution;

public interface IExecutionLiveValidator
{
    Task<ExecutionLiveValidationResult> ValidateStepAsync(
        string sourceRoot,
        string destinationRoot,
        MigrationPlan migrationPlan,
        ExecutionJournalEntry step,
        CancellationToken cancellationToken = default);
}
