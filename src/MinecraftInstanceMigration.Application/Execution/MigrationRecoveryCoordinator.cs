using System.Collections.Concurrent;
using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Execution;

namespace MinecraftInstanceMigration.Application.Execution;

public sealed class MigrationRecoveryCoordinator(
    IExecutionSafetyPlanner safetyPlanner,
    IExecutionJournalPersistence journalPersistence,
    IBackupArtifactValidator backupValidator,
    IRollbackExecutor rollbackExecutor,
    IRollbackAttemptPersistence attemptPersistence) : IMigrationRecoveryCoordinator
{
    private readonly ConcurrentDictionary<Guid, RecoveryAuthorization> authorizations = new();
    private readonly IExecutionSafetyPlanner safetyPlanner = safetyPlanner ?? throw new ArgumentNullException(nameof(safetyPlanner));
    private readonly IExecutionJournalPersistence journalPersistence = journalPersistence ?? throw new ArgumentNullException(nameof(journalPersistence));
    private readonly IBackupArtifactValidator backupValidator = backupValidator ?? throw new ArgumentNullException(nameof(backupValidator));
    private readonly IRollbackExecutor rollbackExecutor = rollbackExecutor ?? throw new ArgumentNullException(nameof(rollbackExecutor));
    private readonly IRollbackAttemptPersistence attemptPersistence = attemptPersistence ?? throw new ArgumentNullException(nameof(attemptPersistence));

    public async Task<MigrationRecoveryDiagnosis> DiagnoseAsync(
        MigrationRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.DestinationRoot) ||
            string.IsNullOrWhiteSpace(request.JournalParent))
        {
            return Blocked(MigrationRecoveryDiagnosisFailureKind.InvalidRequest);
        }

        ExecutionJournalDraft draft = safetyPlanner.CreateJournalDraft(request.MigrationPlan);
        ExecutionJournalReadResult loaded;
        try
        {
            loaded = await journalPersistence.LoadAsync(
                request.ExecutionJournal,
                draft,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new MigrationRecoveryDiagnosis(MigrationRecoveryDiagnosisStatus.Cancelled);
        }
        catch (Exception)
        {
            return Blocked(MigrationRecoveryDiagnosisFailureKind.JournalUnavailable);
        }

        if (loaded.Status == ExecutionJournalReadStatus.Cancelled)
        {
            return new MigrationRecoveryDiagnosis(MigrationRecoveryDiagnosisStatus.Cancelled);
        }

        if (!loaded.IsLoaded || loaded.Snapshot is null)
        {
            return Blocked(MigrationRecoveryDiagnosisFailureKind.JournalUnavailable);
        }

        bool backupRequired = loaded.Snapshot.Steps.Any(step =>
            step.Outcome == ExecutionStepOutcome.Applied &&
            step.Operation == ExecutionOperationKind.Replace);
        BackupArtifactValidationResult? backupValidation = null;
        if (backupRequired)
        {
            if (!string.IsNullOrWhiteSpace(request.BackupRoot))
            {
                try
                {
                    backupValidation = await backupValidator.ValidateAsync(
                        request.BackupRoot,
                        request.BackupPlan,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return new MigrationRecoveryDiagnosis(MigrationRecoveryDiagnosisStatus.Cancelled);
                }
                catch (Exception)
                {
                    backupValidation = null;
                }
            }
        }

        RollbackPlan plan = safetyPlanner.CreateRollbackPlan(
            draft,
            loaded.Snapshot,
            backupValidation);
        int applied = loaded.Snapshot.Steps.Count(step => step.Outcome == ExecutionStepOutcome.Applied);
        int failed = loaded.Snapshot.Steps.Count(step => step.Outcome == ExecutionStepOutcome.Failed);
        int uncertain = loaded.Snapshot.Steps.Count(step => step.Outcome == ExecutionStepOutcome.Uncertain);
        int notStarted = loaded.Snapshot.Steps.Count(step => step.Outcome == ExecutionStepOutcome.NotStarted);
        int deletes = plan.Entries.Count(entry => entry.Action == RollbackActionKind.DeleteCreatedEntry);
        int restores = plan.Entries.Count(entry => entry.Action == RollbackActionKind.RestoreFromBackup);

        MigrationRecoveryDiagnosisStatus status = plan.Status switch
        {
            RollbackPlanStatus.Ready => MigrationRecoveryDiagnosisStatus.RollbackAvailable,
            RollbackPlanStatus.NotRequired => MigrationRecoveryDiagnosisStatus.NotRequired,
            RollbackPlanStatus.RecoveryRequired => MigrationRecoveryDiagnosisStatus.ManualRecoveryRequired,
            _ => MigrationRecoveryDiagnosisStatus.Blocked,
        };

        var diagnosis = new MigrationRecoveryDiagnosis(
            status,
            plan,
            status == MigrationRecoveryDiagnosisStatus.Blocked
                ? MigrationRecoveryDiagnosisFailureKind.InvalidEvidence
                : null,
            applied,
            failed,
            uncertain,
            notStarted,
            deletes,
            restores,
            backupRequired);

        if (diagnosis.CanRollback)
        {
            Guid authorizationId = Guid.NewGuid();
            authorizations.TryAdd(
                authorizationId,
                new RecoveryAuthorization(request, diagnosis.RollbackPlan!));
            diagnosis = diagnosis with
            {
                AuthorizationId = authorizationId,
                AuthorizedRequest = request,
            };
        }

        return diagnosis;
    }

    public async Task<MigrationRecoveryResult> ExecuteAsync(
        MigrationRecoveryRequest request,
        MigrationRecoveryDiagnosis diagnosis,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(diagnosis);

        if (!diagnosis.CanRollback ||
            diagnosis.RollbackPlan is null ||
            diagnosis.AuthorizationId == Guid.Empty ||
            !authorizations.TryRemove(diagnosis.AuthorizationId, out RecoveryAuthorization? authorization) ||
            authorization.Request != request ||
            !ReferenceEquals(authorization.RollbackPlan, diagnosis.RollbackPlan) ||
            diagnosis.AuthorizedRequest != request)
        {
            return new MigrationRecoveryResult(MigrationRecoveryOutcome.Blocked);
        }

        RollbackExecutionResult execution;
        try
        {
            execution = await rollbackExecutor.ExecuteAsync(
                new RollbackExecutionRequest(
                    request.DestinationRoot,
                    request.BackupRoot,
                    request.BackupPlan,
                    request.JournalParent,
                    diagnosis.RollbackPlan),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new MigrationRecoveryResult(MigrationRecoveryOutcome.Cancelled);
        }
        catch (Exception)
        {
            return new MigrationRecoveryResult(MigrationRecoveryOutcome.Uncertain);
        }

        if (execution.Attempt is null)
        {
            return new MigrationRecoveryResult(MapWithoutAttempt(execution), execution);
        }

        RollbackAttemptReadResult loaded;
        try
        {
            loaded = await attemptPersistence.LoadAsync(
                execution.Attempt,
                diagnosis.RollbackPlan,
                CancellationToken.None);
        }
        catch (Exception)
        {
            return new MigrationRecoveryResult(MigrationRecoveryOutcome.Uncertain, execution);
        }

        if (!loaded.IsLoaded ||
            loaded.Snapshot is null ||
            loaded.Snapshot.Steps.Count != diagnosis.RollbackPlan.Entries.Count)
        {
            return new MigrationRecoveryResult(MigrationRecoveryOutcome.Uncertain, execution);
        }

        int applied = loaded.Snapshot.Steps.Count(step => step.Outcome == RollbackAttemptStepOutcome.Applied);
        int rejected = loaded.Snapshot.Steps.Count(step => step.Outcome == RollbackAttemptStepOutcome.GuardRejected);
        int failed = loaded.Snapshot.Steps.Count(step => step.Outcome == RollbackAttemptStepOutcome.Failed);
        int uncertain = loaded.Snapshot.Steps.Count(step => step.Outcome == RollbackAttemptStepOutcome.Uncertain);
        MigrationRecoveryOutcome outcome = uncertain > 0
            ? MigrationRecoveryOutcome.Uncertain
            : failed > 0
                ? MigrationRecoveryOutcome.Failed
                : rejected > 0
                    ? MigrationRecoveryOutcome.GuardRejected
                    : applied == loaded.Snapshot.Steps.Count
                        ? MigrationRecoveryOutcome.Applied
                        : MapWithoutAttempt(execution);

        return new MigrationRecoveryResult(outcome, execution, applied, rejected, failed, uncertain);
    }

    private static MigrationRecoveryDiagnosis Blocked(MigrationRecoveryDiagnosisFailureKind failure) =>
        new(MigrationRecoveryDiagnosisStatus.Blocked, FailureKind: failure);

    private static MigrationRecoveryOutcome MapWithoutAttempt(RollbackExecutionResult result) =>
        result.Status switch
        {
            RollbackExecutionStatus.NotRequired => MigrationRecoveryOutcome.Applied,
            RollbackExecutionStatus.Completed => MigrationRecoveryOutcome.Uncertain,
            RollbackExecutionStatus.Cancelled => MigrationRecoveryOutcome.Cancelled,
            RollbackExecutionStatus.Blocked when result.FailureKind == RollbackExecutionFailureKind.GuardRejected =>
                MigrationRecoveryOutcome.Uncertain,
            RollbackExecutionStatus.Blocked => MigrationRecoveryOutcome.Blocked,
            _ => MigrationRecoveryOutcome.Uncertain,
        };

    private sealed record RecoveryAuthorization(
        MigrationRecoveryRequest Request,
        RollbackPlan RollbackPlan);
}
