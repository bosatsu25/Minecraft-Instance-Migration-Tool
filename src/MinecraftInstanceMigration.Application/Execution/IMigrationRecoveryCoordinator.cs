namespace MinecraftInstanceMigration.Application.Execution;

public interface IMigrationRecoveryCoordinator
{
    Task<MigrationRecoveryDiagnosis> DiagnoseAsync(
        MigrationRecoveryRequest request,
        CancellationToken cancellationToken = default);

    Task<MigrationRecoveryResult> ExecuteAsync(
        MigrationRecoveryRequest request,
        MigrationRecoveryDiagnosis diagnosis,
        CancellationToken cancellationToken = default);
}
