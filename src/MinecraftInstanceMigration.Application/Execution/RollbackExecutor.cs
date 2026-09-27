using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Domain.Execution;

namespace MinecraftInstanceMigration.Application.Execution;

public sealed class RollbackExecutor(
    IBackupArtifactValidator backupValidator,
    IRollbackAttemptPersistence attemptPersistence,
    IRollbackStorage storage) : IRollbackExecutor
{
    private readonly IBackupArtifactValidator backupValidator =
        backupValidator ?? throw new ArgumentNullException(nameof(backupValidator));
    private readonly IRollbackAttemptPersistence attemptPersistence =
        attemptPersistence ?? throw new ArgumentNullException(nameof(attemptPersistence));
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

        if (string.IsNullOrWhiteSpace(request.JournalParent))
        {
            return new RollbackExecutionResult(
                RollbackExecutionStatus.Blocked,
                RollbackExecutionFailureKind.JournalRequired);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new RollbackExecutionResult(
                RollbackExecutionStatus.Cancelled);
        }

        RollbackAttemptWriteResult created;
        try
        {
            created = await attemptPersistence.CreateAsync(
                request.JournalParent,
                request.DestinationRoot,
                request.BackupRoot,
                request.RollbackPlan,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return new RollbackExecutionResult(
                RollbackExecutionStatus.Cancelled);
        }
        catch (Exception)
        {
            return new RollbackExecutionResult(
                RollbackExecutionStatus.Blocked,
                RollbackExecutionFailureKind.JournalFailure);
        }

        if (!created.IsSuccess || created.Attempt is null)
        {
            return created.Status == RollbackAttemptWriteStatus.Cancelled
                ? new RollbackExecutionResult(
                    RollbackExecutionStatus.Cancelled)
                : new RollbackExecutionResult(
                    RollbackExecutionStatus.Blocked,
                    RollbackExecutionFailureKind.JournalFailure);
        }

        RollbackAttemptReference attempt = created.Attempt;
        int completedActions = 0;

        foreach (RollbackPlanEntry action in request.RollbackPlan.Entries)
        {
            RollbackBackupEvidence? backupEvidence = null;

            if (cancellationToken.IsCancellationRequested)
            {
                return CancellationResult(
                    completedActions,
                    attempt);
            }

            if (action.Action == RollbackActionKind.RestoreFromBackup)
            {
                if (string.IsNullOrWhiteSpace(request.BackupRoot) ||
                    request.BackupPlan is null ||
                    !request.BackupPlan.CanStartBackup)
                {
                    return BeforeActionFailure(
                        RollbackExecutionFailureKind.BackupRequired,
                        completedActions,
                        attempt);
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
                    return CancellationResult(
                        completedActions,
                        attempt);
                }
                catch (Exception)
                {
                    return BeforeActionFailure(
                        RollbackExecutionFailureKind.BackupInvalid,
                        completedActions,
                        attempt);
                }

                if (validation.Status == BackupArtifactValidationStatus.Cancelled)
                {
                    return CancellationResult(
                        completedActions,
                        attempt);
                }

                if (!validation.IsValid ||
                    validation.Verification is null)
                {
                    return BeforeActionFailure(
                        RollbackExecutionFailureKind.BackupInvalid,
                        completedActions,
                        attempt);
                }

                backupEvidence = new RollbackBackupEvidence(
                    request.BackupPlan,
                    validation.Verification);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return CancellationResult(
                    completedActions,
                    attempt);
            }

            RollbackAttemptWriteResult started;
            try
            {
                started = await attemptPersistence.MarkActionStartedAsync(
                    attempt,
                    request.RollbackPlan,
                    action.Order,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return CancellationResult(
                    completedActions,
                    attempt);
            }
            catch (Exception)
            {
                return BeforeActionFailure(
                    RollbackExecutionFailureKind.JournalFailure,
                    completedActions,
                    attempt);
            }

            if (!started.IsSuccess)
            {
                return started.Status == RollbackAttemptWriteStatus.Cancelled
                    ? CancellationResult(
                        completedActions,
                        attempt)
                    : BeforeActionFailure(
                        RollbackExecutionFailureKind.JournalFailure,
                        completedActions,
                        attempt);
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
                return await FailAfterStartedAsync(
                    attempt,
                    request.RollbackPlan,
                    action,
                    RollbackStorageFailureKind.IoFailure,
                    completedActions,
                    RollbackExecutionFailureKind.StorageFailure);
            }

            if (result.Status == RollbackStorageStatus.GuardRejected)
            {
                RollbackStorageFailureKind failureKind =
                    result.FailureKind ??
                    RollbackStorageFailureKind.IoFailure;

                RollbackAttemptWriteResult rejected;
                try
                {
                    rejected =
                        await attemptPersistence.MarkActionGuardRejectedAsync(
                            attempt,
                            request.RollbackPlan,
                            action.Order,
                            failureKind,
                            CancellationToken.None);
                }
                catch (Exception)
                {
                    return RecoveryRequired(
                        RollbackExecutionFailureKind.JournalFailure,
                        completedActions,
                        attempt);
                }

                if (!rejected.IsSuccess)
                {
                    return RecoveryRequired(
                        RollbackExecutionFailureKind.JournalFailure,
                        completedActions,
                        attempt);
                }

                return BeforeActionFailure(
                    RollbackExecutionFailureKind.GuardRejected,
                    completedActions,
                    attempt);
            }

            if (!result.IsApplied)
            {
                return await FailAfterStartedAsync(
                    attempt,
                    request.RollbackPlan,
                    action,
                    result.FailureKind ??
                        RollbackStorageFailureKind.IoFailure,
                    completedActions,
                    RollbackExecutionFailureKind.StorageFailure);
            }

            RollbackAttemptWriteResult applied;
            try
            {
                applied = await attemptPersistence.MarkActionAppliedAsync(
                    attempt,
                    request.RollbackPlan,
                    action.Order,
                    CancellationToken.None);
            }
            catch (Exception)
            {
                return RecoveryRequired(
                    RollbackExecutionFailureKind.JournalFailure,
                    completedActions,
                    attempt);
            }

            if (!applied.IsSuccess)
            {
                return RecoveryRequired(
                    RollbackExecutionFailureKind.JournalFailure,
                    completedActions,
                    attempt);
            }

            completedActions++;
        }

        return new RollbackExecutionResult(
            RollbackExecutionStatus.Completed,
            CompletedActions: completedActions,
            Attempt: attempt);
    }

    private async Task<RollbackExecutionResult> FailAfterStartedAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        RollbackPlanEntry action,
        RollbackStorageFailureKind storageFailure,
        int completedActions,
        RollbackExecutionFailureKind resultFailure)
    {
        try
        {
            RollbackAttemptWriteResult failed =
                await attemptPersistence.MarkActionFailedAsync(
                    attempt,
                    plan,
                    action.Order,
                    storageFailure,
                    CancellationToken.None);

            if (!failed.IsSuccess)
            {
                return RecoveryRequired(
                    RollbackExecutionFailureKind.JournalFailure,
                    completedActions,
                    attempt);
            }
        }
        catch (Exception)
        {
            return RecoveryRequired(
                RollbackExecutionFailureKind.JournalFailure,
                completedActions,
                attempt);
        }

        return RecoveryRequired(
            resultFailure,
            completedActions,
            attempt);
    }

    private static RollbackExecutionResult CancellationResult(
        int completedActions,
        RollbackAttemptReference attempt) =>
        completedActions == 0
            ? new RollbackExecutionResult(
                RollbackExecutionStatus.Cancelled,
                Attempt: attempt)
            : RecoveryRequired(
                RollbackExecutionFailureKind.CancelledAfterPartialRollback,
                completedActions,
                attempt);

    private static RollbackExecutionResult BeforeActionFailure(
        RollbackExecutionFailureKind failureKind,
        int completedActions,
        RollbackAttemptReference attempt) =>
        completedActions == 0
            ? new RollbackExecutionResult(
                RollbackExecutionStatus.Blocked,
                failureKind,
                Attempt: attempt)
            : RecoveryRequired(
                failureKind,
                completedActions,
                attempt);

    private static RollbackExecutionResult RecoveryRequired(
        RollbackExecutionFailureKind failureKind,
        int completedActions,
        RollbackAttemptReference attempt) =>
        new(
            RollbackExecutionStatus.RecoveryRequired,
            failureKind,
            completedActions,
            attempt);
}
