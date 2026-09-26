namespace MinecraftInstanceMigration.Application.Execution;

public interface IExecutionOrchestrator
{
    Task<ExecutionOrchestrationResult> ExecuteAsync(
        ExecutionOrchestrationRequest request,
        CancellationToken cancellationToken = default);
}
