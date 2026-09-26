using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Execution;

namespace MinecraftInstanceMigration.Application.Execution;

public sealed class ExecutionOrchestrator(
    IExecutionSafetyPlanner safetyPlanner,
    IExecutionLiveValidator liveValidator,
    IBackupPlanner backupPlanner,
    IBackupArtifactValidator backupValidator,
    IExecutionJournalPersistence journalPersistence,
    IExecutionMutationPort mutationPort,
    IExecutionPostWriteVerifier postWriteVerifier) : IExecutionOrchestrator
{
    private readonly IExecutionSafetyPlanner safetyPlanner =
        safetyPlanner ?? throw new ArgumentNullException(nameof(safetyPlanner));
    private readonly IExecutionLiveValidator liveValidator =
        liveValidator ?? throw new ArgumentNullException(nameof(liveValidator));
    private readonly IBackupPlanner backupPlanner =
        backupPlanner ?? throw new ArgumentNullException(nameof(backupPlanner));
    private readonly IBackupArtifactValidator backupValidator =
        backupValidator ?? throw new ArgumentNullException(nameof(backupValidator));
    private readonly IExecutionJournalPersistence journalPersistence =
        journalPersistence ?? throw new ArgumentNullException(nameof(journalPersistence));
    private readonly IExecutionMutationPort mutationPort =
        mutationPort ?? throw new ArgumentNullException(nameof(mutationPort));
    private readonly IExecutionPostWriteVerifier postWriteVerifier =
        postWriteVerifier ?? throw new ArgumentNullException(nameof(postWriteVerifier));

    public async Task<ExecutionOrchestrationResult> ExecuteAsync(
        ExecutionOrchestrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.MigrationPlan);

        ExecutionJournalDraft draft =
            safetyPlanner.CreateJournalDraft(request.MigrationPlan);

        if (draft.Status == ExecutionJournalStatus.NotRequired)
        {
            return new ExecutionOrchestrationResult(
                ExecutionOrchestrationStatus.NotRequired);
        }

        if (!draft.CanStartExecution)
        {
            return Blocked(ExecutionOrchestrationFailureKind.InvalidPlan);
        }

        BackupPlan backupPlan =
            backupPlanner.CreateBackupPlan(request.MigrationPlan);

        if (backupPlan.Status == BackupPlanStatus.Blocked)
        {
            return Blocked(ExecutionOrchestrationFailureKind.InvalidPlan);
        }

        if (backupPlan.RequiresBackup &&
            string.IsNullOrWhiteSpace(request.BackupRoot))
        {
            return Blocked(ExecutionOrchestrationFailureKind.BackupRequired);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new ExecutionOrchestrationResult(
                ExecutionOrchestrationStatus.Cancelled);
        }

        ExecutionJournalWriteResult created;
        try
        {
            created = await journalPersistence.CreateAsync(
                request.JournalParent,
                draft,
                cancellationToken);
        }
        catch (Exception)
        {
            return Failed(ExecutionOrchestrationFailureKind.JournalFailure);
        }

        if (!created.IsSuccess || created.Journal is null)
        {
            return created.Status == ExecutionJournalWriteStatus.Cancelled
                ? new ExecutionOrchestrationResult(
                    ExecutionOrchestrationStatus.Cancelled)
                : Failed(ExecutionOrchestrationFailureKind.JournalFailure);
        }

        ExecutionJournalReference journal = created.Journal;
        int appliedSteps = 0;

        foreach (ExecutionJournalEntry step in draft.Entries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new ExecutionOrchestrationResult(
                    ExecutionOrchestrationStatus.Cancelled,
                    Journal: journal,
                    AppliedSteps: appliedSteps);
            }

            ExecutionLiveValidationResult live;
            try
            {
                live = await liveValidator.ValidateStepAsync(
                    request.SourceRoot,
                    request.DestinationRoot,
                    request.MigrationPlan,
                    step,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new ExecutionOrchestrationResult(
                    ExecutionOrchestrationStatus.Cancelled,
                    Journal: journal,
                    AppliedSteps: appliedSteps);
            }
            catch (Exception)
            {
                return BeforeStartFailure(
                    ExecutionOrchestrationFailureKind.LiveStateChanged,
                    journal,
                    appliedSteps);
            }

            if (!live.IsValid)
            {
                return BeforeStartFailure(
                    ExecutionOrchestrationFailureKind.LiveStateChanged,
                    journal,
                    appliedSteps);
            }

            if (step.Operation == ExecutionOperationKind.Replace)
            {
                BackupArtifactValidationResult backupValidation;
                try
                {
                    backupValidation = await backupValidator.ValidateAsync(
                        request.BackupRoot!,
                        backupPlan,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return new ExecutionOrchestrationResult(
                        ExecutionOrchestrationStatus.Cancelled,
                        Journal: journal,
                        AppliedSteps: appliedSteps);
                }
                catch (Exception)
                {
                    return BeforeStartFailure(
                        ExecutionOrchestrationFailureKind.BackupInvalid,
                        journal,
                        appliedSteps);
                }

                if (!backupValidation.IsValid)
                {
                    return BeforeStartFailure(
                        ExecutionOrchestrationFailureKind.BackupInvalid,
                        journal,
                        appliedSteps);
                }
            }

            ExecutionJournalWriteResult started;
            try
            {
                started = await journalPersistence.MarkStepStartedAsync(
                    journal,
                    draft,
                    step.Sequence,
                    cancellationToken);
            }
            catch (Exception)
            {
                return BeforeStartFailure(
                    ExecutionOrchestrationFailureKind.JournalFailure,
                    journal,
                    appliedSteps);
            }

            if (!started.IsSuccess)
            {
                return started.Status == ExecutionJournalWriteStatus.Cancelled
                    ? new ExecutionOrchestrationResult(
                        ExecutionOrchestrationStatus.Cancelled,
                        Journal: journal,
                        AppliedSteps: appliedSteps)
                    : BeforeStartFailure(
                        ExecutionOrchestrationFailureKind.JournalFailure,
                        journal,
                        appliedSteps);
            }

            ExecutionMutationResult mutation;
            try
            {
                mutation = await mutationPort.ApplyAsync(
                    request.SourceRoot,
                    request.DestinationRoot,
                    step,
                    cancellationToken);
            }
            catch (Exception)
            {
                return await RecoverAfterStartedAsync(
                    journal,
                    draft,
                    step,
                    appliedSteps,
                    ExecutionOrchestrationFailureKind.MutationFailed);
            }

            if (mutation.Status != ExecutionMutationStatus.Applied)
            {
                return await RecoverAfterStartedAsync(
                    journal,
                    draft,
                    step,
                    appliedSteps,
                    ExecutionOrchestrationFailureKind.MutationFailed);
            }

            ExecutionPostWriteVerificationResult verification;
            try
            {
                verification = await postWriteVerifier.VerifyAsync(
                    request.DestinationRoot,
                    step,
                    CancellationToken.None);
            }
            catch (Exception)
            {
                return await RecoverAfterStartedAsync(
                    journal,
                    draft,
                    step,
                    appliedSteps,
                    ExecutionOrchestrationFailureKind.PostWriteVerificationFailed);
            }

            if (!verification.IsVerified)
            {
                return await RecoverAfterStartedAsync(
                    journal,
                    draft,
                    step,
                    appliedSteps,
                    ExecutionOrchestrationFailureKind.PostWriteVerificationFailed);
            }

            ExecutionJournalWriteResult applied;
            try
            {
                applied = await journalPersistence.MarkStepAppliedAsync(
                    journal,
                    draft,
                    step.Sequence,
                    verification.Fingerprint!,
                    CancellationToken.None);
            }
            catch (Exception)
            {
                return RecoveryRequired(
                    ExecutionOrchestrationFailureKind.JournalFailure,
                    journal,
                    appliedSteps);
            }

            if (!applied.IsSuccess)
            {
                return RecoveryRequired(
                    ExecutionOrchestrationFailureKind.JournalFailure,
                    journal,
                    appliedSteps);
            }

            appliedSteps++;
        }

        return new ExecutionOrchestrationResult(
            ExecutionOrchestrationStatus.Completed,
            Journal: journal,
            AppliedSteps: appliedSteps);
    }

    private async Task<ExecutionOrchestrationResult> RecoverAfterStartedAsync(
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft,
        ExecutionJournalEntry step,
        int appliedSteps,
        ExecutionOrchestrationFailureKind failureKind)
    {
        try
        {
            ExecutionJournalWriteResult failed =
                await journalPersistence.MarkStepFailedAsync(
                    journal,
                    draft,
                    step.Sequence,
                    CancellationToken.None);

            if (!failed.IsSuccess)
            {
                return RecoveryRequired(
                    ExecutionOrchestrationFailureKind.JournalFailure,
                    journal,
                    appliedSteps);
            }
        }
        catch (Exception)
        {
            return RecoveryRequired(
                ExecutionOrchestrationFailureKind.JournalFailure,
                journal,
                appliedSteps);
        }

        return RecoveryRequired(
            failureKind,
            journal,
            appliedSteps);
    }

    private static ExecutionOrchestrationResult BeforeStartFailure(
        ExecutionOrchestrationFailureKind failureKind,
        ExecutionJournalReference journal,
        int appliedSteps) =>
        appliedSteps == 0
            ? new ExecutionOrchestrationResult(
                ExecutionOrchestrationStatus.Blocked,
                failureKind,
                journal)
            : RecoveryRequired(
                failureKind,
                journal,
                appliedSteps);

    private static ExecutionOrchestrationResult Blocked(
        ExecutionOrchestrationFailureKind failureKind) =>
        new(
            ExecutionOrchestrationStatus.Blocked,
            failureKind);

    private static ExecutionOrchestrationResult Failed(
        ExecutionOrchestrationFailureKind failureKind) =>
        new(
            ExecutionOrchestrationStatus.Failed,
            failureKind);

    private static ExecutionOrchestrationResult RecoveryRequired(
        ExecutionOrchestrationFailureKind failureKind,
        ExecutionJournalReference journal,
        int appliedSteps) =>
        new(
            ExecutionOrchestrationStatus.RecoveryRequired,
            failureKind,
            journal,
            appliedSteps);
}
