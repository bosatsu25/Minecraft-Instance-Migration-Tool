using MinecraftInstanceMigration.Application.Inspection;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Execution;

public sealed class ExecutionLiveValidator(IInstanceInspector inspector) : IExecutionLiveValidator
{
    private readonly IInstanceInspector inspector =
        inspector ?? throw new ArgumentNullException(nameof(inspector));

    public async Task<ExecutionLiveValidationResult> ValidateStepAsync(
        string sourceRoot,
        string destinationRoot,
        MigrationPlan migrationPlan,
        ExecutionJournalEntry step,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(migrationPlan);
        ArgumentNullException.ThrowIfNull(step);

        InstanceInspectionResult source =
            await inspector.InspectAsync(sourceRoot, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        InstanceInspectionResult destination =
            await inspector.InspectAsync(destinationRoot, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return ExecutionLiveValidationPolicy.ValidateStep(
            migrationPlan,
            step,
            source,
            destination);
    }
}
