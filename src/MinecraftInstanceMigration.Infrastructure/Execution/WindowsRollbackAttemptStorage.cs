using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Infrastructure.FileSystem;

namespace MinecraftInstanceMigration.Infrastructure.Execution;

public sealed class WindowsRollbackAttemptStorage : IRollbackAttemptStorage
{
    private const string FileNamePrefix = "mim-rollback-";

    // Rollback evidence is a separate append-only journal; the parent only receives new files.
    private const uint JournalParentAccess = NtFileApi.FileAddFile | NtFileApi.FileWriteAttributes;

    public Task<RollbackAttemptWriteResult> CreateAsync(
        string journalParent,
        string destinationRoot,
        string? backupRoot,
        RollbackPlan plan,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => Create(journalParent, destinationRoot, backupRoot, plan, cancellationToken),
            CancellationToken.None);

    public Task<RollbackAttemptWriteResult> MarkActionStartedAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        int order,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => AppendTransition(attempt, plan, order, "Started", null, cancellationToken),
            CancellationToken.None);

    public Task<RollbackAttemptWriteResult> MarkActionAppliedAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        int order,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => AppendTransition(attempt, plan, order, "Applied", null, cancellationToken),
            CancellationToken.None);

    public Task<RollbackAttemptWriteResult> MarkActionGuardRejectedAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        int order,
        RollbackStorageFailureKind failureKind,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => AppendTransition(
                attempt,
                plan,
                order,
                "GuardRejected",
                failureKind.ToString(),
                cancellationToken),
            CancellationToken.None);

    public Task<RollbackAttemptWriteResult> MarkActionFailedAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        int order,
        RollbackStorageFailureKind failureKind,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => AppendTransition(attempt, plan, order, "Failed", failureKind.ToString(), cancellationToken),
            CancellationToken.None);

    public Task<RollbackAttemptReadResult> LoadAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => Load(attempt, plan, cancellationToken),
            CancellationToken.None);

    private static RollbackAttemptWriteResult Create(
        string journalParent,
        string destinationRoot,
        string? backupRoot,
        RollbackPlan plan,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Rollback attempt storage requires Windows.");
        }

        if (!IsValidPlan(plan))
        {
            return InvalidWrite(RollbackAttemptPersistenceFailureKind.InvalidPlan);
        }

        if (!WindowsPathRules.TryNormalizeLocalDirectory(journalParent, out string journal) ||
            !WindowsPathRules.TryNormalizeLocalDirectory(destinationRoot, out string destination))
        {
            return InvalidWrite(RollbackAttemptPersistenceFailureKind.InvalidPath);
        }

        string? backup = null;
        if (!string.IsNullOrWhiteSpace(backupRoot) &&
            !WindowsPathRules.TryNormalizeLocalDirectory(backupRoot, out backup))
        {
            return InvalidWrite(RollbackAttemptPersistenceFailureKind.InvalidPath);
        }

        // Rollback evidence must never live inside data that rollback deletes or restores from.
        if (WindowsPathRules.IsEqualOrDescendant(journal, destination) ||
            backup is not null && WindowsPathRules.IsEqualOrDescendant(journal, backup) ||
            backup is not null && WindowsPathRules.RootsOverlap(destination, backup))
        {
            return InvalidWrite(RollbackAttemptPersistenceFailureKind.UnsafeWorkspace);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new RollbackAttemptWriteResult(RollbackAttemptWriteStatus.Cancelled);
        }

        RollbackAttemptReference? reference = null;

        try
        {
            using WindowsExecutionTree.HeldDirectory destinationDirectory =
                WindowsExecutionTree.OpenDirectoryChain(destination, WindowsExecutionTree.ReadOnlyDirectoryAccess);
            using WindowsExecutionTree.HeldDirectory journalDirectory =
                WindowsExecutionTree.OpenDirectoryChain(journal, JournalParentAccess);
            using WindowsExecutionTree.HeldDirectory? backupDirectory = backup is null
                ? null
                : WindowsExecutionTree.OpenDirectoryChain(backup, WindowsExecutionTree.ReadOnlyDirectoryAccess);

            // Repeat the containment check on canonical handle paths so drive aliases cannot bypass it.
            if (WindowsExecutionTree.IsPhysicalEqualOrDescendant(journalDirectory.Root, destinationDirectory.Root) ||
                backupDirectory is not null &&
                    WindowsExecutionTree.IsPhysicalEqualOrDescendant(journalDirectory.Root, backupDirectory.Root) ||
                backupDirectory is not null &&
                    WindowsExecutionTree.PhysicalRootsOverlap(destinationDirectory, backupDirectory))
            {
                return InvalidWrite(RollbackAttemptPersistenceFailureKind.UnsafeWorkspace);
            }

            cancellationToken.ThrowIfCancellationRequested();

            string attemptId = Guid.NewGuid().ToString("N");
            string fileName = $"{FileNamePrefix}{attemptId}.jsonl";
            reference = new RollbackAttemptReference(attemptId, Path.Combine(journal, fileName));

            using FileStream stream = ChecksummedJournal.CreateNew(journalDirectory.Root, fileName);

            JournalPayload payload = new(
                Index: 0,
                Kind: "Header",
                PreviousChecksum: null,
                SchemaVersion: RollbackAttemptSnapshot.CurrentSchemaVersion,
                AttemptId: attemptId,
                Entries: plan.Entries.Select(PersistedEntry.From).ToArray(),
                Order: null,
                FailureKind: null);

            ChecksummedJournal.AppendDurably(stream, Envelope(payload));

            return new RollbackAttemptWriteResult(RollbackAttemptWriteStatus.Succeeded, reference);
        }
        catch (OperationCanceledException)
        {
            return new RollbackAttemptWriteResult(RollbackAttemptWriteStatus.Cancelled, reference);
        }
        catch (RollbackAttemptStorageException error)
        {
            return WriteFailure(error.Kind, reference);
        }
        catch (Exception error) when (FileSystemErrors.TryClassify(error, out FileSystemFailureKind kind))
        {
            return WriteFailure(MapFailure(kind), reference);
        }
    }

    private static RollbackAttemptWriteResult AppendTransition(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        int order,
        string kind,
        string? failureKind,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Rollback attempt storage requires Windows.");
        }

        if (!IsValidPlan(plan) ||
            order < 0 ||
            order >= plan.Entries.Count ||
            plan.Entries[order].Order != order)
        {
            return InvalidWrite(RollbackAttemptPersistenceFailureKind.InvalidPlan, attempt);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new RollbackAttemptWriteResult(RollbackAttemptWriteStatus.Cancelled, attempt);
        }

        try
        {
            using FileStream stream = OpenJournalFile(attempt, writable: true);
            ParsedJournal parsed = ParseJournal(stream, attempt, plan);

            bool validTransition = kind switch
            {
                "Started" => CanStart(parsed.States, order),
                "Applied" or "GuardRejected" or "Failed" => parsed.States[order] == PersistedActionState.Started,
                _ => false,
            };

            bool validFailure = kind switch
            {
                "GuardRejected" or "Failed" => TryParseStorageFailure(failureKind, out _),
                _ => failureKind is null,
            };

            if (!validTransition || !validFailure)
            {
                return InvalidWrite(RollbackAttemptPersistenceFailureKind.JournalStateInvalid, attempt);
            }

            cancellationToken.ThrowIfCancellationRequested();
            ChecksummedJournal.PrepareAppend(stream, parsed.Contents);

            JournalPayload payload = new(
                Index: parsed.Contents.Records.Count,
                Kind: kind,
                PreviousChecksum: parsed.Contents.Records[^1].Checksum,
                SchemaVersion: null,
                AttemptId: null,
                Entries: null,
                Order: order,
                FailureKind: failureKind);

            ChecksummedJournal.AppendDurably(stream, Envelope(payload));

            return new RollbackAttemptWriteResult(RollbackAttemptWriteStatus.Succeeded, attempt);
        }
        catch (OperationCanceledException)
        {
            return new RollbackAttemptWriteResult(RollbackAttemptWriteStatus.Cancelled, attempt);
        }
        catch (RollbackAttemptStorageException error)
        {
            return WriteFailure(error.Kind, attempt);
        }
        catch (Exception error) when (FileSystemErrors.TryClassify(error, out FileSystemFailureKind failure))
        {
            return WriteFailure(MapFailure(failure), attempt);
        }
    }

    private static RollbackAttemptReadResult Load(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Rollback attempt storage requires Windows.");
        }

        if (!IsValidPlan(plan))
        {
            return InvalidRead(RollbackAttemptPersistenceFailureKind.InvalidPlan);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new RollbackAttemptReadResult(RollbackAttemptReadStatus.Cancelled);
        }

        try
        {
            using FileStream stream = OpenJournalFile(attempt, writable: false);
            ParsedJournal parsed = ParseJournal(stream, attempt, plan);

            cancellationToken.ThrowIfCancellationRequested();

            return new RollbackAttemptReadResult(RollbackAttemptReadStatus.Loaded, parsed.Snapshot);
        }
        catch (OperationCanceledException)
        {
            return new RollbackAttemptReadResult(RollbackAttemptReadStatus.Cancelled);
        }
        catch (RollbackAttemptStorageException error)
        {
            return ReadFailure(error.Kind);
        }
        catch (Exception error) when (FileSystemErrors.TryClassify(error, out FileSystemFailureKind kind))
        {
            return ReadFailure(MapFailure(kind));
        }
    }

    private static ParsedJournal ParseJournal(
        FileStream stream,
        RollbackAttemptReference attempt,
        RollbackPlan plan)
    {
        JournalContents<JournalEnvelope> contents;
        try
        {
            contents = ChecksummedJournal.Read<JournalEnvelope>(
                stream,
                envelope => envelope.Payload is not null &&
                    ChecksummedJournal.ChecksumMatches(envelope.Checksum, envelope.Payload));
        }
        catch (JournalFormatException)
        {
            throw InvalidJournal();
        }

        IReadOnlyList<JournalEnvelope> envelopes = contents.Records;
        ValidateHeader(envelopes[0], attempt, plan);

        var states = Enumerable.Repeat(PersistedActionState.NotStarted, plan.Entries.Count).ToArray();
        var failures = new RollbackStorageFailureKind?[plan.Entries.Count];
        string previousChecksum = envelopes[0].Checksum;

        for (int index = 1; index < envelopes.Count; index++)
        {
            JournalEnvelope envelope = envelopes[index];
            JournalPayload payload = envelope.Payload;

            if (payload.Index != index ||
                !string.Equals(payload.PreviousChecksum, previousChecksum, StringComparison.OrdinalIgnoreCase) ||
                payload.SchemaVersion is not null ||
                payload.AttemptId is not null ||
                payload.Entries is not null ||
                payload.Order is null ||
                payload.Order < 0 ||
                payload.Order >= plan.Entries.Count)
            {
                throw InvalidJournal();
            }

            int order = payload.Order.Value;

            switch (payload.Kind)
            {
                case "Started":
                    if (payload.FailureKind is not null || !CanStart(states, order))
                    {
                        throw InvalidState();
                    }

                    states[order] = PersistedActionState.Started;
                    break;

                case "Applied":
                    if (payload.FailureKind is not null || states[order] != PersistedActionState.Started)
                    {
                        throw InvalidState();
                    }

                    states[order] = PersistedActionState.Applied;
                    break;

                case "GuardRejected":
                    if (states[order] != PersistedActionState.Started ||
                        !TryParseStorageFailure(payload.FailureKind, out RollbackStorageFailureKind rejectedKind))
                    {
                        throw InvalidState();
                    }

                    states[order] = PersistedActionState.GuardRejected;
                    failures[order] = rejectedKind;
                    break;

                case "Failed":
                    if (states[order] != PersistedActionState.Started ||
                        !TryParseStorageFailure(payload.FailureKind, out RollbackStorageFailureKind failedKind))
                    {
                        throw InvalidState();
                    }

                    states[order] = PersistedActionState.Failed;
                    failures[order] = failedKind;
                    break;

                default:
                    throw InvalidJournal();
            }

            previousChecksum = envelope.Checksum;
        }

        var steps = new RollbackAttemptStep[plan.Entries.Count];
        for (int index = 0; index < plan.Entries.Count; index++)
        {
            RollbackPlanEntry entry = plan.Entries[index];

            // A durable Started without a terminal record is never replayed or skipped automatically.
            RollbackAttemptStepOutcome outcome = states[index] switch
            {
                PersistedActionState.NotStarted => RollbackAttemptStepOutcome.NotStarted,
                PersistedActionState.Started => RollbackAttemptStepOutcome.Uncertain,
                PersistedActionState.Applied => RollbackAttemptStepOutcome.Applied,
                PersistedActionState.GuardRejected => RollbackAttemptStepOutcome.GuardRejected,
                PersistedActionState.Failed => RollbackAttemptStepOutcome.Failed,
                _ => throw InvalidState(),
            };

            steps[index] = new RollbackAttemptStep(entry.Order, entry.Name, entry.Action, outcome, failures[index]);
        }

        return new ParsedJournal(
            new RollbackAttemptSnapshot(RollbackAttemptSnapshot.CurrentSchemaVersion, steps),
            states,
            contents);
    }

    // The attempt is bound to the exact reviewed plan, including each post-write fingerprint guard.
    private static void ValidateHeader(
        JournalEnvelope header,
        RollbackAttemptReference attempt,
        RollbackPlan plan)
    {
        JournalPayload payload = header.Payload;

        if (payload.Index != 0 ||
            !string.Equals(payload.Kind, "Header", StringComparison.Ordinal) ||
            payload.PreviousChecksum is not null ||
            payload.SchemaVersion != RollbackAttemptSnapshot.CurrentSchemaVersion ||
            !string.Equals(payload.AttemptId, attempt.AttemptId, StringComparison.Ordinal) ||
            payload.Entries is null ||
            payload.Entries.Count != plan.Entries.Count ||
            payload.Order is not null ||
            payload.FailureKind is not null)
        {
            throw InvalidJournal();
        }

        for (int index = 0; index < plan.Entries.Count; index++)
        {
            PersistedEntry actual = payload.Entries[index];
            RollbackPlanEntry expected = plan.Entries[index];

            if (actual is null ||
                actual.Order != index ||
                expected.Order != index ||
                !string.Equals(actual.Name, expected.Name, StringComparison.Ordinal) ||
                !string.Equals(actual.ExpectedKind, expected.ExpectedKind.ToString(), StringComparison.Ordinal) ||
                !string.Equals(actual.Operation, expected.Operation.ToString(), StringComparison.Ordinal) ||
                !string.Equals(actual.Action, expected.Action.ToString(), StringComparison.Ordinal) ||
                actual.Fingerprint is null ||
                !actual.Fingerprint.Equals(PersistedFingerprint.From(expected.ExpectedCurrentFingerprint!)))
            {
                throw InvalidJournal();
            }
        }
    }

    private static bool IsValidPlan(RollbackPlan? plan)
    {
        if (plan is null ||
            plan.Status != RollbackPlanStatus.Ready ||
            plan.Entries.Count == 0 ||
            plan.Blockers.Count != 0)
        {
            return false;
        }

        for (int index = 0; index < plan.Entries.Count; index++)
        {
            RollbackPlanEntry entry = plan.Entries[index];

            if (entry is null ||
                !WindowsPathRules.IsSingleName(entry.Name) ||
                !Enum.IsDefined(entry.ExpectedKind) ||
                entry.Order != index ||
                entry.ExpectedCurrentFingerprint is null ||
                entry.RecoveryReason is not null)
            {
                return false;
            }

            bool validAction = entry.Action switch
            {
                RollbackActionKind.DeleteCreatedEntry => entry.Operation == ExecutionOperationKind.Copy,
                RollbackActionKind.RestoreFromBackup => entry.Operation == ExecutionOperationKind.Replace,
                _ => false,
            };

            if (!validAction)
            {
                return false;
            }
        }

        return true;
    }

    // An action may start only after every earlier action is durably Applied and no later one has begun.
    private static bool CanStart(
        IReadOnlyList<PersistedActionState> states,
        int order) =>
        states[order] == PersistedActionState.NotStarted &&
        states.Take(order).All(state => state == PersistedActionState.Applied) &&
        states.Skip(order + 1).All(state => state == PersistedActionState.NotStarted);

    private static FileStream OpenJournalFile(
        RollbackAttemptReference? attempt,
        bool writable)
    {
        if (!ChecksummedJournal.TryResolveReference(
                attempt?.AttemptId,
                attempt?.JournalPath,
                FileNamePrefix,
                out string parent,
                out string fileName))
        {
            throw new RollbackAttemptStorageException(RollbackAttemptPersistenceFailureKind.InvalidReference);
        }

        using WindowsExecutionTree.HeldDirectory directory =
            WindowsExecutionTree.OpenDirectoryChain(parent, WindowsExecutionTree.ReadOnlyDirectoryAccess);
        return ChecksummedJournal.OpenExisting(directory.Root, fileName, writable);
    }

    private static JournalEnvelope Envelope(JournalPayload payload) =>
        new(payload, ChecksummedJournal.Checksum(payload));

    private static bool TryParseStorageFailure(
        string? value,
        out RollbackStorageFailureKind kind) =>
        Enum.TryParse(value, ignoreCase: false, out kind) && Enum.IsDefined(kind);

    private static RollbackAttemptWriteResult InvalidWrite(
        RollbackAttemptPersistenceFailureKind kind,
        RollbackAttemptReference? attempt = null) =>
        new(RollbackAttemptWriteStatus.Invalid, attempt, kind);

    private static RollbackAttemptReadResult InvalidRead(
        RollbackAttemptPersistenceFailureKind kind) =>
        new(RollbackAttemptReadStatus.Invalid, FailureKind: kind);

    private static bool IsInvalidRatherThanFailed(RollbackAttemptPersistenceFailureKind kind) =>
        kind is
            RollbackAttemptPersistenceFailureKind.InvalidPlan or
            RollbackAttemptPersistenceFailureKind.InvalidReference or
            RollbackAttemptPersistenceFailureKind.InvalidPath or
            RollbackAttemptPersistenceFailureKind.UnsafeWorkspace or
            RollbackAttemptPersistenceFailureKind.ReparsePoint or
            RollbackAttemptPersistenceFailureKind.JournalFormatInvalid or
            RollbackAttemptPersistenceFailureKind.JournalStateInvalid;

    private static RollbackAttemptWriteResult WriteFailure(
        RollbackAttemptPersistenceFailureKind kind,
        RollbackAttemptReference? attempt) =>
        IsInvalidRatherThanFailed(kind)
            ? InvalidWrite(kind, attempt)
            : new RollbackAttemptWriteResult(RollbackAttemptWriteStatus.Failed, attempt, kind);

    private static RollbackAttemptReadResult ReadFailure(
        RollbackAttemptPersistenceFailureKind kind) =>
        IsInvalidRatherThanFailed(kind)
            ? InvalidRead(kind)
            : new RollbackAttemptReadResult(RollbackAttemptReadStatus.Failed, FailureKind: kind);

    private static RollbackAttemptPersistenceFailureKind MapFailure(FileSystemFailureKind kind) =>
        kind switch
        {
            FileSystemFailureKind.InvalidPath => RollbackAttemptPersistenceFailureKind.InvalidPath,
            FileSystemFailureKind.Missing or
            FileSystemFailureKind.Changed or
            FileSystemFailureKind.NotRegularFile => RollbackAttemptPersistenceFailureKind.InvalidReference,
            FileSystemFailureKind.ReparsePoint => RollbackAttemptPersistenceFailureKind.ReparsePoint,
            FileSystemFailureKind.AccessDenied => RollbackAttemptPersistenceFailureKind.AccessDenied,
            _ => RollbackAttemptPersistenceFailureKind.IoFailure,
        };

    private static RollbackAttemptStorageException InvalidJournal() =>
        new(RollbackAttemptPersistenceFailureKind.JournalFormatInvalid);

    private static RollbackAttemptStorageException InvalidState() =>
        new(RollbackAttemptPersistenceFailureKind.JournalStateInvalid);

    private enum PersistedActionState
    {
        NotStarted,
        Started,
        Applied,
        GuardRejected,
        Failed,
    }

    private sealed class RollbackAttemptStorageException(
        RollbackAttemptPersistenceFailureKind kind) : Exception
    {
        internal RollbackAttemptPersistenceFailureKind Kind { get; } = kind;
    }

    private sealed record PersistedFingerprint(
        int FileCount,
        int DirectoryCount,
        long TotalBytes,
        string Sha256)
    {
        internal static PersistedFingerprint From(ExecutionContentFingerprint fingerprint) =>
            new(fingerprint.FileCount, fingerprint.DirectoryCount, fingerprint.TotalBytes, fingerprint.Sha256);
    }

    private sealed record PersistedEntry(
        int Order,
        string Name,
        string ExpectedKind,
        string Operation,
        string Action,
        PersistedFingerprint? Fingerprint)
    {
        internal static PersistedEntry From(RollbackPlanEntry entry) =>
            new(
                entry.Order,
                entry.Name,
                entry.ExpectedKind.ToString(),
                entry.Operation.ToString(),
                entry.Action.ToString(),
                entry.ExpectedCurrentFingerprint is null
                    ? null
                    : PersistedFingerprint.From(entry.ExpectedCurrentFingerprint));
    }

    private sealed record JournalPayload(
        int Index,
        string Kind,
        string? PreviousChecksum,
        int? SchemaVersion,
        string? AttemptId,
        IReadOnlyList<PersistedEntry>? Entries,
        int? Order,
        string? FailureKind);

    private sealed record JournalEnvelope(
        JournalPayload Payload,
        string Checksum);

    private sealed record ParsedJournal(
        RollbackAttemptSnapshot Snapshot,
        PersistedActionState[] States,
        JournalContents<JournalEnvelope> Contents);
}
