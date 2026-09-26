namespace MinecraftInstanceMigration.Application.Execution;

public interface IRollbackExecutor
{
    Task<RollbackExecutionResult> ExecuteAsync(
        RollbackExecutionRequest request,
        CancellationToken cancellationToken = default);
}
