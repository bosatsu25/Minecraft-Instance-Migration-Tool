using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Infrastructure.FileSystem;

namespace MinecraftInstanceMigration.Infrastructure.Execution;

public sealed class WindowsExecutionJournalStorage : IExecutionJournalStorage
{
    private const string FileNamePrefix = "mim-journal-";

    // The journal parent only needs to receive the new create-only journal file.
    private const uint JournalParentAccess = NtFileApi.FileAddFile | NtFileApi.FileWriteAttributes;

    public Task<ExecutionJournalWriteResult> CreateAsync(
        string journalParent,
        ExecutionJournalDraft draft,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => Create(journalParent, draft, cancellationToken),
            CancellationToken.None);

    public Task<ExecutionJournalWriteResult> MarkStepStartedAsync(
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft,
        int sequence,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => AppendTransition(journal, draft, sequence, "Started", null, cancellationToken),
            CancellationToken.None);

    public Task<ExecutionJournalWriteResult> MarkStepAppliedAsync(
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft,
        int sequence,
        ExecutionContentFingerprint fingerprint,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => AppendTransition(
                journal,
                draft,
                sequence,
                "Applied",
                PersistedFingerprint.From(fingerprint),
                cancellationToken),
            CancellationToken.None);

    public Task<ExecutionJournalWriteResult> MarkStepFailedAsync(
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft,
        int sequence,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => AppendTransition(journal, draft, sequence, "Failed", null, cancellationToken),
            CancellationToken.None);

    public Task<ExecutionJournalReadResult> LoadAsync(
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => Load(journal, draft, cancellationToken),
            CancellationToken.None);

    private static ExecutionJournalWriteResult Create(
        string journalParent,
        ExecutionJournalDraft draft,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Execution journal storage requires Windows.");
        }

        if (!draft.CanStartExecution)
        {
            return InvalidWrite(ExecutionJournalPersistenceFailureKind.InvalidDraft);
        }

        if (!WindowsPathRules.TryNormalizeLocalDirectory(journalParent, out string normalizedParent))
        {
            return InvalidWrite(ExecutionJournalPersistenceFailureKind.InvalidPath);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new ExecutionJournalWriteResult(ExecutionJournalWriteStatus.Cancelled);
        }

        ExecutionJournalReference? reference = null;

        try
        {
            using WindowsExecutionTree.HeldDirectory parent = OpenParent(normalizedParent, JournalParentAccess);

            string journalId = Guid.NewGuid().ToString("N");
            string fileName = $"{FileNamePrefix}{journalId}.jsonl";
            reference = new ExecutionJournalReference(journalId, Path.Combine(normalizedParent, fileName));

            using FileStream stream = CreateJournalFile(parent, fileName);

            JournalPayload headerPayload = new(
                Index: 0,
                Kind: "Header",
                PreviousChecksum: null,
                SchemaVersion: draft.SchemaVersion,
                JournalId: journalId,
                Entries: draft.Entries
                    .Select(entry => new PersistedEntry(
                        entry.Sequence,
                        entry.Name,
                        entry.ExpectedKind.ToString(),
                        entry.Operation.ToString()))
                    .ToArray(),
                Sequence: null,
                Fingerprint: null);

            ChecksummedJournal.AppendDurably(stream, Envelope(headerPayload));

            return new ExecutionJournalWriteResult(ExecutionJournalWriteStatus.Succeeded, reference);
        }
        catch (OperationCanceledException)
        {
            return new ExecutionJournalWriteResult(ExecutionJournalWriteStatus.Cancelled, reference);
        }
        catch (JournalStorageException error)
        {
            return WriteFailure(error.Kind, reference);
        }
        catch (Exception error) when (FileSystemErrors.TryClassify(error, out FileSystemFailureKind kind))
        {
            return WriteFailure(MapFailure(kind, missingIsInvalidReference: false), reference);
        }
    }

    private static ExecutionJournalWriteResult AppendTransition(
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft,
        int sequence,
        string kind,
        PersistedFingerprint? fingerprint,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Execution journal storage requires Windows.");
        }

        if (!draft.CanStartExecution)
        {
            return InvalidWrite(ExecutionJournalPersistenceFailureKind.InvalidDraft);
        }

        if (sequence < 0 || sequence >= draft.Entries.Count)
        {
            return InvalidWrite(ExecutionJournalPersistenceFailureKind.JournalStateInvalid);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new ExecutionJournalWriteResult(ExecutionJournalWriteStatus.Cancelled, journal);
        }

        try
        {
            using FileStream stream = OpenJournalFile(journal, writable: true);
            ParsedJournal parsed = ParseJournal(stream, journal, draft);

            bool validTransition = kind switch
            {
                "Started" => CanStart(parsed.States, sequence),
                "Applied" or "Failed" => parsed.States[sequence] == PersistedStepState.Started,
                _ => false,
            };

            if (!validTransition ||
                kind == "Applied" && fingerprint is null ||
                kind != "Applied" && fingerprint is not null)
            {
                return InvalidWrite(ExecutionJournalPersistenceFailureKind.JournalStateInvalid, journal);
            }

            cancellationToken.ThrowIfCancellationRequested();
            ChecksummedJournal.PrepareAppend(stream, parsed.Contents);

            JournalPayload payload = new(
                Index: parsed.Contents.Records.Count,
                Kind: kind,
                PreviousChecksum: parsed.Contents.Records[^1].Checksum,
                SchemaVersion: null,
                JournalId: null,
                Entries: null,
                Sequence: sequence,
                Fingerprint: fingerprint);

            ChecksummedJournal.AppendDurably(stream, Envelope(payload));

            return new ExecutionJournalWriteResult(ExecutionJournalWriteStatus.Succeeded, journal);
        }
        catch (OperationCanceledException)
        {
            return new ExecutionJournalWriteResult(ExecutionJournalWriteStatus.Cancelled, journal);
        }
        catch (JournalStorageException error)
        {
            return WriteFailure(error.Kind, journal);
        }
        catch (Exception error) when (FileSystemErrors.TryClassify(error, out FileSystemFailureKind failure))
        {
            return WriteFailure(MapFailure(failure, missingIsInvalidReference: false), journal);
        }
    }

    private static ExecutionJournalReadResult Load(
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Execution journal storage requires Windows.");
        }

        if (!draft.CanStartExecution)
        {
            return InvalidRead(ExecutionJournalPersistenceFailureKind.InvalidDraft);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new ExecutionJournalReadResult(ExecutionJournalReadStatus.Cancelled);
        }

        try
        {
            using FileStream stream = OpenJournalFile(journal, writable: false);
            ParsedJournal parsed = ParseJournal(stream, journal, draft);

            cancellationToken.ThrowIfCancellationRequested();

            return new ExecutionJournalReadResult(ExecutionJournalReadStatus.Loaded, parsed.Snapshot);
        }
        catch (OperationCanceledException)
        {
            return new ExecutionJournalReadResult(ExecutionJournalReadStatus.Cancelled);
        }
        catch (JournalStorageException error)
        {
            return ReadFailure(error.Kind);
        }
        catch (Exception error) when (FileSystemErrors.TryClassify(error, out FileSystemFailureKind kind))
        {
            return ReadFailure(MapFailure(kind, missingIsInvalidReference: false));
        }
    }

    private static ParsedJournal ParseJournal(
        FileStream stream,
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft)
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
            throw new JournalStorageException(ExecutionJournalPersistenceFailureKind.JournalFormatInvalid);
        }

        IReadOnlyList<JournalEnvelope> envelopes = contents.Records;
        ValidateHeader(envelopes[0], journal, draft);

        var states = Enumerable.Repeat(PersistedStepState.NotStarted, draft.Entries.Count).ToArray();
        var fingerprints = new ExecutionContentFingerprint?[draft.Entries.Count];
        string previousChecksum = envelopes[0].Checksum;

        for (int index = 1; index < envelopes.Count; index++)
        {
            JournalEnvelope envelope = envelopes[index];
            JournalPayload payload = envelope.Payload;

            if (payload.Index != index ||
                !string.Equals(payload.PreviousChecksum, previousChecksum, StringComparison.OrdinalIgnoreCase) ||
                payload.SchemaVersion is not null ||
                payload.JournalId is not null ||
                payload.Entries is not null ||
                payload.Sequence is null ||
                payload.Sequence < 0 ||
                payload.Sequence >= draft.Entries.Count)
            {
                throw new JournalStorageException(ExecutionJournalPersistenceFailureKind.JournalFormatInvalid);
            }

            int sequence = payload.Sequence.Value;

            switch (payload.Kind)
            {
                case "Started":
                    if (payload.Fingerprint is not null || !CanStart(states, sequence))
                    {
                        throw new JournalStorageException(ExecutionJournalPersistenceFailureKind.JournalStateInvalid);
                    }

                    states[sequence] = PersistedStepState.Started;
                    break;

                case "Applied":
                    if (states[sequence] != PersistedStepState.Started || payload.Fingerprint is null)
                    {
                        throw new JournalStorageException(ExecutionJournalPersistenceFailureKind.JournalStateInvalid);
                    }

                    fingerprints[sequence] = ToDomainFingerprint(payload.Fingerprint);
                    states[sequence] = PersistedStepState.Applied;
                    break;

                case "Failed":
                    if (states[sequence] != PersistedStepState.Started || payload.Fingerprint is not null)
                    {
                        throw new JournalStorageException(ExecutionJournalPersistenceFailureKind.JournalStateInvalid);
                    }

                    states[sequence] = PersistedStepState.Failed;
                    break;

                default:
                    throw new JournalStorageException(ExecutionJournalPersistenceFailureKind.JournalFormatInvalid);
            }

            previousChecksum = envelope.Checksum;
        }

        var steps = new ExecutionJournalStep[draft.Entries.Count];
        for (int index = 0; index < draft.Entries.Count; index++)
        {
            ExecutionJournalEntry draftEntry = draft.Entries[index];

            // A durable Started without a terminal record cannot prove what happened.
            ExecutionStepOutcome outcome = states[index] switch
            {
                PersistedStepState.NotStarted => ExecutionStepOutcome.NotStarted,
                PersistedStepState.Started => ExecutionStepOutcome.Uncertain,
                PersistedStepState.Applied => ExecutionStepOutcome.Applied,
                PersistedStepState.Failed => ExecutionStepOutcome.Failed,
                _ => throw new JournalStorageException(ExecutionJournalPersistenceFailureKind.JournalStateInvalid),
            };

            steps[index] = new ExecutionJournalStep(
                draftEntry.Sequence,
                draftEntry.Name,
                draftEntry.Operation,
                outcome,
                fingerprints[index]);
        }

        return new ParsedJournal(
            new ExecutionJournalSnapshot(draft.SchemaVersion, steps),
            states,
            contents);
    }

    private static void ValidateHeader(
        JournalEnvelope header,
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft)
    {
        JournalPayload payload = header.Payload;

        if (payload.Index != 0 ||
            !string.Equals(payload.Kind, "Header", StringComparison.Ordinal) ||
            payload.PreviousChecksum is not null ||
            payload.SchemaVersion != draft.SchemaVersion ||
            !string.Equals(payload.JournalId, journal.JournalId, StringComparison.Ordinal) ||
            payload.Entries is null ||
            payload.Entries.Count != draft.Entries.Count ||
            payload.Sequence is not null ||
            payload.Fingerprint is not null)
        {
            throw new JournalStorageException(ExecutionJournalPersistenceFailureKind.JournalFormatInvalid);
        }

        for (int index = 0; index < draft.Entries.Count; index++)
        {
            ExecutionJournalEntry expected = draft.Entries[index];
            PersistedEntry actual = payload.Entries[index];

            if (actual is null ||
                actual.Sequence != index ||
                expected.Sequence != index ||
                !string.Equals(actual.Name, expected.Name, StringComparison.Ordinal) ||
                !string.Equals(actual.ExpectedKind, expected.ExpectedKind.ToString(), StringComparison.Ordinal) ||
                !string.Equals(actual.Operation, expected.Operation.ToString(), StringComparison.Ordinal))
            {
                throw new JournalStorageException(ExecutionJournalPersistenceFailureKind.JournalFormatInvalid);
            }
        }
    }

    // A step may start only after every earlier step is durably Applied and no later step has begun.
    private static bool CanStart(
        IReadOnlyList<PersistedStepState> states,
        int sequence) =>
        states[sequence] == PersistedStepState.NotStarted &&
        states.Take(sequence).All(state => state == PersistedStepState.Applied) &&
        states.Skip(sequence + 1).All(state => state == PersistedStepState.NotStarted);

    private static ExecutionContentFingerprint ToDomainFingerprint(PersistedFingerprint fingerprint)
    {
        try
        {
            return new ExecutionContentFingerprint(
                fingerprint.FileCount,
                fingerprint.DirectoryCount,
                fingerprint.TotalBytes,
                fingerprint.Sha256);
        }
        catch (ArgumentException)
        {
            throw new JournalStorageException(ExecutionJournalPersistenceFailureKind.JournalFormatInvalid);
        }
    }

    private static JournalEnvelope Envelope(JournalPayload payload) =>
        new(payload, ChecksummedJournal.Checksum(payload));

    private static WindowsExecutionTree.HeldDirectory OpenParent(string path, uint finalAccess)
    {
        try
        {
            return WindowsExecutionTree.OpenDirectoryChain(path, finalAccess);
        }
        catch (FileSystemException error)
        {
            throw new JournalStorageException(MapFailure(error.Kind, missingIsInvalidReference: false));
        }
    }

    private static FileStream CreateJournalFile(
        WindowsExecutionTree.HeldDirectory parent,
        string fileName)
    {
        try
        {
            return ChecksummedJournal.CreateNew(parent.Root, fileName);
        }
        catch (FileSystemException error)
        {
            throw new JournalStorageException(MapFailure(error.Kind, missingIsInvalidReference: false));
        }
    }

    private static FileStream OpenJournalFile(
        ExecutionJournalReference? journal,
        bool writable)
    {
        if (!ChecksummedJournal.TryResolveReference(
                journal?.JournalId,
                journal?.JournalPath,
                FileNamePrefix,
                out string normalizedParent,
                out string fileName))
        {
            throw new JournalStorageException(ExecutionJournalPersistenceFailureKind.InvalidReference);
        }

        using WindowsExecutionTree.HeldDirectory parent =
            OpenParent(normalizedParent, WindowsExecutionTree.ReadOnlyDirectoryAccess);
        try
        {
            return ChecksummedJournal.OpenExisting(parent.Root, fileName, writable);
        }
        catch (FileSystemException error)
        {
            throw new JournalStorageException(MapFailure(error.Kind, missingIsInvalidReference: true));
        }
    }

    // A missing journal file is an invalid reference; a missing parent directory is an invalid path.
    private static ExecutionJournalPersistenceFailureKind MapFailure(
        FileSystemFailureKind kind,
        bool missingIsInvalidReference) =>
        kind switch
        {
            FileSystemFailureKind.ReparsePoint => ExecutionJournalPersistenceFailureKind.ReparsePoint,
            FileSystemFailureKind.Missing when missingIsInvalidReference =>
                ExecutionJournalPersistenceFailureKind.InvalidReference,
            FileSystemFailureKind.Missing or FileSystemFailureKind.InvalidPath =>
                ExecutionJournalPersistenceFailureKind.InvalidPath,
            FileSystemFailureKind.NotRegularFile => ExecutionJournalPersistenceFailureKind.InvalidReference,
            FileSystemFailureKind.AccessDenied => ExecutionJournalPersistenceFailureKind.AccessDenied,
            _ => ExecutionJournalPersistenceFailureKind.IoFailure,
        };

    private static ExecutionJournalWriteResult InvalidWrite(
        ExecutionJournalPersistenceFailureKind kind,
        ExecutionJournalReference? journal = null) =>
        new(ExecutionJournalWriteStatus.Invalid, journal, kind);

    private static ExecutionJournalReadResult InvalidRead(
        ExecutionJournalPersistenceFailureKind kind) =>
        new(ExecutionJournalReadStatus.Invalid, FailureKind: kind);

    private static bool IsInvalidRatherThanFailed(ExecutionJournalPersistenceFailureKind kind) =>
        kind is
            ExecutionJournalPersistenceFailureKind.InvalidDraft or
            ExecutionJournalPersistenceFailureKind.InvalidReference or
            ExecutionJournalPersistenceFailureKind.InvalidPath or
            ExecutionJournalPersistenceFailureKind.ReparsePoint or
            ExecutionJournalPersistenceFailureKind.JournalFormatInvalid or
            ExecutionJournalPersistenceFailureKind.JournalStateInvalid;

    private static ExecutionJournalWriteResult WriteFailure(
        ExecutionJournalPersistenceFailureKind kind,
        ExecutionJournalReference? journal) =>
        IsInvalidRatherThanFailed(kind)
            ? InvalidWrite(kind, journal)
            : new ExecutionJournalWriteResult(ExecutionJournalWriteStatus.Failed, journal, kind);

    private static ExecutionJournalReadResult ReadFailure(
        ExecutionJournalPersistenceFailureKind kind) =>
        IsInvalidRatherThanFailed(kind)
            ? InvalidRead(kind)
            : new ExecutionJournalReadResult(ExecutionJournalReadStatus.Failed, FailureKind: kind);

    private enum PersistedStepState
    {
        NotStarted,
        Started,
        Applied,
        Failed,
    }

    private sealed class JournalStorageException(
        ExecutionJournalPersistenceFailureKind kind) : Exception
    {
        public ExecutionJournalPersistenceFailureKind Kind { get; } = kind;
    }

    private sealed record PersistedEntry(
        int Sequence,
        string Name,
        string ExpectedKind,
        string Operation);

    private sealed record PersistedFingerprint(
        int FileCount,
        int DirectoryCount,
        long TotalBytes,
        string Sha256)
    {
        public static PersistedFingerprint From(ExecutionContentFingerprint fingerprint) =>
            new(fingerprint.FileCount, fingerprint.DirectoryCount, fingerprint.TotalBytes, fingerprint.Sha256);
    }

    private sealed record JournalPayload(
        int Index,
        string Kind,
        string? PreviousChecksum,
        int? SchemaVersion,
        string? JournalId,
        IReadOnlyList<PersistedEntry>? Entries,
        int? Sequence,
        PersistedFingerprint? Fingerprint);

    private sealed record JournalEnvelope(
        JournalPayload Payload,
        string Checksum);

    private sealed record ParsedJournal(
        ExecutionJournalSnapshot Snapshot,
        PersistedStepState[] States,
        JournalContents<JournalEnvelope> Contents);
}
