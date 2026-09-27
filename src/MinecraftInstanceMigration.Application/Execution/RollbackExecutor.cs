using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Domain.Execution;

namespace MinecraftInstanceMigration.Application.Execution;

public sealed class RollbackExecutor(
    IBackupArtifactValidator backupValidator,
    IRollbackStorage storage) : IRollbackExecutor
{
    private readonly IBackupArtifactValidator backupValidator =
        backupValidator ?? throw new ArgumentNullException(nameof(backupValidator));
    private readonly IRollbackStorage storage =
        storage ?? throw new ArgumentNullException(nameof(storage));

    public async Task<RollbackExecutionResult> ExecuteAsync(
        RollbackExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RollbackPlan);

        if (request.RollbackPlan.Status == RollbackPlanStatus.NotRequired)
        {
            return new RollbackExecutionResult(
                RollbackExecutionStatus.NotRequired);
        }

        if (request.RollbackPlan.Status == RollbackPlanStatus.RecoveryRequired)
        {
            return new RollbackExecutionResult(
                RollbackExecutionStatus.RecoveryRequired,
                RollbackExecutionFailureKind.InvalidPlan);
        }

        if (!request.RollbackPlan.CanAttemptAutomaticRollback)
        {
            return new RollbackExecutionResult(
                RollbackExecutionStatus.Blocked,
                RollbackExecutionFailureKind.InvalidPlan);
        }

        int completedActions = 0;

        foreach (RollbackPlanEntry action in request.RollbackPlan.Entries)
        {
            RollbackBackupEvidence? backupEvidence = null;

            if (cancellationToken.IsCancellationRequested)
            {
                return CancellationResult(completedActions);
            }

            if (action.Action == RollbackActionKind.RestoreFromBackup)
            {
                if (string.IsNullOrWhiteSpace(request.BackupRoot) ||
                    request.BackupPlan is null ||
                    !request.BackupPlan.CanStartBackup)
                {
                    return BeforeActionFailure(
                        RollbackExecutionFailureKind.BackupRequired,
                        completedActions);
                }

                BackupArtifactValidationResult validation;
                try
                {
                    validation = await backupValidator.ValidateAsync(
                        request.BackupRoot,
                        request.BackupPlan,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return CancellationResult(completedActions);
                }
                catch (Exception)
                {
                    return BeforeActionFailure(
                        RollbackExecutionFailureKind.BackupInvalid,
                        completedActions);
                }

                if (validation.Status == BackupArtifactValidationStatus.Cancelled)
                {
                    return CancellationResult(completedActions);
                }

                if (!validation.IsValid || validation.Verification is null)
                {
                    return BeforeActionFailure(
                        RollbackExecutionFailureKind.BackupInvalid,
                        completedActions);
                }

                backupEvidence = new RollbackBackupEvidence(
                    request.BackupPlan,
                    validation.Verification);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return CancellationResult(completedActions);
            }

            RollbackStorageResult result;
            try
            {
                result = await storage.ApplyAsync(
                    request.DestinationRoot,
                    request.BackupRoot,
                    backupEvidence,
                    action,
                    CancellationToken.None);
            }
            catch (Exception)
            {
                return new RollbackExecutionResult(
                    RollbackExecutionStatus.RecoveryRequired,
                    RollbackExecutionFailureKind.StorageFailure,
                    completedActions);
            }

            if (result.Status == RollbackStorageStatus.GuardRejected)
            {
                return BeforeActionFailure(
                    RollbackExecutionFailureKind.GuardRejected,
                    completedActions);
            }

            if (!result.IsApplied)
            {
                return new RollbackExecutionResult(
                    RollbackExecutionStatus.RecoveryRequired,
                    RollbackExecutionFailureKind.StorageFailure,
                    completedActions);
            }

            completedActions++;
        }

        return new RollbackExecutionResult(
            RollbackExecutionStatus.Completed,
            CompletedActions: completedActions);
    }

    private static RollbackExecutionResult CancellationResult(
        int completedActions) =>
        completedActions == 0
            ? new RollbackExecutionResult(
                RollbackExecutionStatus.Cancelled)
            : new RollbackExecutionResult(
                RollbackExecutionStatus.RecoveryRequired,
                RollbackExecutionFailureKind.CancelledAfterPartialRollback,
                completedActions);

    private static RollbackExecutionResult BeforeActionFailure(
        RollbackExecutionFailureKind failureKind,
        int completedActions) =>
        completedActions == 0
            ? new RollbackExecutionResult(
                RollbackExecutionStatus.Blocked,
                failureKind)
            : new RollbackExecutionResult(
                RollbackExecutionStatus.RecoveryRequired,
                failureKind,
                completedActions);
}
