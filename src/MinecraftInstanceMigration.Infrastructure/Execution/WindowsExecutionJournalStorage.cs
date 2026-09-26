using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Infrastructure.Backup;

namespace MinecraftInstanceMigration.Infrastructure.Execution;

public sealed class WindowsExecutionJournalStorage : IExecutionJournalStorage
{
    private const int MaximumJournalBytes = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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
            () => AppendTransition(
                journal,
                draft,
                sequence,
                "Started",
                null,
                cancellationToken),
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
            () => AppendTransition(
                journal,
                draft,
                sequence,
                "Failed",
                null,
                cancellationToken),
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

        if (!TryNormalizeLocalDirectoryPath(journalParent, out string normalizedParent))
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
            using HeldDirectory parent = OpenDirectoryChain(normalizedParent, writableFinal: true);

            string journalId = Guid.NewGuid().ToString("N");
            string fileName = $"mim-journal-{journalId}.jsonl";
            reference = new ExecutionJournalReference(
                journalId,
                Path.Combine(normalizedParent, fileName));

            using FileStream stream = CreateJournalFile(parent.Root, fileName);

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

            JournalEnvelope header = Envelope(headerPayload);
            WriteEnvelopeDurably(stream, header);

            return new ExecutionJournalWriteResult(
                ExecutionJournalWriteStatus.Succeeded,
                reference);
        }
        catch (OperationCanceledException)
        {
            return new ExecutionJournalWriteResult(
                ExecutionJournalWriteStatus.Cancelled,
                reference);
        }
        catch (JournalStorageException error)
        {
            return WriteFailure(error.Kind, reference);
        }
        catch (UnauthorizedAccessException)
        {
            return WriteFailure(
                ExecutionJournalPersistenceFailureKind.AccessDenied,
                reference);
        }
        catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 5)
        {
            return WriteFailure(
                ExecutionJournalPersistenceFailureKind.AccessDenied,
                reference);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return WriteFailure(
                ExecutionJournalPersistenceFailureKind.IoFailure,
                reference);
        }
        catch (IOException)
        {
            return WriteFailure(
                ExecutionJournalPersistenceFailureKind.IoFailure,
                reference);
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

            if (!validTransition)
            {
                return InvalidWrite(
                    ExecutionJournalPersistenceFailureKind.JournalStateInvalid,
                    journal);
            }

            if (kind == "Applied" && fingerprint is null ||
                kind != "Applied" && fingerprint is not null)
            {
                return InvalidWrite(
                    ExecutionJournalPersistenceFailureKind.JournalStateInvalid,
                    journal);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (parsed.HasTornTail)
            {
                stream.SetLength(parsed.ValidLength);
                stream.Position = parsed.ValidLength;
                stream.Flush(flushToDisk: true);
            }
            else
            {
                stream.Position = parsed.ValidLength;
            }

            JournalPayload payload = new(
                Index: parsed.NextIndex,
                Kind: kind,
                PreviousChecksum: parsed.LastChecksum,
                SchemaVersion: null,
                JournalId: null,
                Entries: null,
                Sequence: sequence,
                Fingerprint: fingerprint);

            WriteEnvelopeDurably(stream, Envelope(payload));

            return new ExecutionJournalWriteResult(
                ExecutionJournalWriteStatus.Succeeded,
                journal);
        }
        catch (OperationCanceledException)
        {
            return new ExecutionJournalWriteResult(
                ExecutionJournalWriteStatus.Cancelled,
                journal);
        }
        catch (JournalStorageException error)
        {
            return WriteFailure(error.Kind, journal);
        }
        catch (UnauthorizedAccessException)
        {
            return WriteFailure(
                ExecutionJournalPersistenceFailureKind.AccessDenied,
                journal);
        }
        catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 5)
        {
            return WriteFailure(
                ExecutionJournalPersistenceFailureKind.AccessDenied,
                journal);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return WriteFailure(
                ExecutionJournalPersistenceFailureKind.IoFailure,
                journal);
        }
        catch (IOException)
        {
            return WriteFailure(
                ExecutionJournalPersistenceFailureKind.IoFailure,
                journal);
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

            return new ExecutionJournalReadResult(
                ExecutionJournalReadStatus.Loaded,
                parsed.Snapshot);
        }
        catch (OperationCanceledException)
        {
            return new ExecutionJournalReadResult(ExecutionJournalReadStatus.Cancelled);
        }
        catch (JournalStorageException error)
        {
            return ReadFailure(error.Kind);
        }
        catch (UnauthorizedAccessException)
        {
            return ReadFailure(ExecutionJournalPersistenceFailureKind.AccessDenied);
        }
        catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 5)
        {
            return ReadFailure(ExecutionJournalPersistenceFailureKind.AccessDenied);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return ReadFailure(ExecutionJournalPersistenceFailureKind.IoFailure);
        }
        catch (IOException)
        {
            return ReadFailure(ExecutionJournalPersistenceFailureKind.IoFailure);
        }
    }

    private static ParsedJournal ParseJournal(
        FileStream stream,
        ExecutionJournalReference journal,
        ExecutionJournalDraft draft)
    {
        if (stream.Length <= 0 || stream.Length > MaximumJournalBytes)
        {
            throw new JournalStorageException(
                ExecutionJournalPersistenceFailureKind.JournalFormatInvalid);
        }

        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.Position = 0;
        int readOffset = 0;
        while (readOffset < bytes.Length)
        {
            int read = stream.Read(bytes, readOffset, bytes.Length - readOffset);
            if (read == 0)
            {
                throw new JournalStorageException(
                    ExecutionJournalPersistenceFailureKind.JournalFormatInvalid);
            }

            readOffset += read;
        }

        int lastNewline = Array.LastIndexOf(bytes, (byte)'\n');
        if (lastNewline < 0)
        {
            throw new JournalStorageException(
                ExecutionJournalPersistenceFailureKind.JournalFormatInvalid);
        }

        int validLength = lastNewline + 1;
        bool hasTornTail = validLength != bytes.Length;

        var envelopes = new List<JournalEnvelope>();
        int start = 0;
        while (start < validLength)
        {
            int newline = Array.IndexOf(bytes, (byte)'\n', start, validLength - start);
            if (newline < 0)
            {
                throw new JournalStorageException(
                    ExecutionJournalPersistenceFailureKind.JournalFormatInvalid);
            }

            int length = newline - start;
            if (length > 0 && bytes[newline - 1] == (byte)'\r')
            {
                length--;
            }

            if (length <= 0)
            {
                throw new JournalStorageException(
                    ExecutionJournalPersistenceFailureKind.JournalFormatInvalid);
            }

            JournalEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<JournalEnvelope>(
                    bytes.AsSpan(start, length),
                    JsonOptions);
            }
            catch (JsonException)
            {
                throw new JournalStorageException(
                    ExecutionJournalPersistenceFailureKind.JournalFormatInvalid);
            }

            if (envelope?.Payload is null ||
                !IsSha256(envelope.Checksum) ||
                !string.Equals(
                    envelope.Checksum,
                    ComputeChecksum(envelope.Payload),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new JournalStorageException(
                    ExecutionJournalPersistenceFailureKind.JournalFormatInvalid);
            }

            envelopes.Add(envelope);
            start = newline + 1;
        }

        if (envelopes.Count == 0)
        {
            throw new JournalStorageException(
                ExecutionJournalPersistenceFailureKind.JournalFormatInvalid);
        }

        ValidateHeader(envelopes[0], journal, draft);

        var states = Enumerable
            .Repeat(PersistedStepState.NotStarted, draft.Entries.Count)
            .ToArray();
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
                throw new JournalStorageException(
                    ExecutionJournalPersistenceFailureKind.JournalFormatInvalid);
            }

            int sequence = payload.Sequence.Value;

            switch (payload.Kind)
            {
                case "Started":
                    if (payload.Fingerprint is not null || !CanStart(states, sequence))
                    {
                        throw new JournalStorageException(
                            ExecutionJournalPersistenceFailureKind.JournalStateInvalid);
                    }

                    states[sequence] = PersistedStepState.Started;
                    break;

                case "Applied":
                    if (states[sequence] != PersistedStepState.Started ||
                        payload.Fingerprint is null)
                    {
                        throw new JournalStorageException(
                            ExecutionJournalPersistenceFailureKind.JournalStateInvalid);
                    }

                    fingerprints[sequence] = ToDomainFingerprint(payload.Fingerprint);
                    states[sequence] = PersistedStepState.Applied;
                    break;

                case "Failed":
                    if (states[sequence] != PersistedStepState.Started ||
                        payload.Fingerprint is not null)
                    {
                        throw new JournalStorageException(
                            ExecutionJournalPersistenceFailureKind.JournalStateInvalid);
                    }

                    states[sequence] = PersistedStepState.Failed;
                    break;

                default:
                    throw new JournalStorageException(
                        ExecutionJournalPersistenceFailureKind.JournalFormatInvalid);
            }

            previousChecksum = envelope.Checksum;
        }

        var steps = new ExecutionJournalStep[draft.Entries.Count];
        for (int index = 0; index < draft.Entries.Count; index++)
        {
            ExecutionJournalEntry draftEntry = draft.Entries[index];
            ExecutionStepOutcome outcome = states[index] switch
            {
                PersistedStepState.NotStarted => ExecutionStepOutcome.NotStarted,
                PersistedStepState.Started => ExecutionStepOutcome.Uncertain,
                PersistedStepState.Applied => ExecutionStepOutcome.Applied,
                PersistedStepState.Failed => ExecutionStepOutcome.Failed,
                _ => throw new JournalStorageException(
                    ExecutionJournalPersistenceFailureKind.JournalStateInvalid),
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
            envelopes.Count,
            previousChecksum,
            validLength,
            hasTornTail);
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
            throw new JournalStorageException(
                ExecutionJournalPersistenceFailureKind.JournalFormatInvalid);
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
                throw new JournalStorageException(
                    ExecutionJournalPersistenceFailureKind.JournalFormatInvalid);
            }
        }
    }

    private static bool CanStart(
        IReadOnlyList<PersistedStepState> states,
        int sequence)
    {
        if (states[sequence] != PersistedStepState.NotStarted)
        {
            return false;
        }

        for (int index = 0; index < sequence; index++)
        {
            if (states[index] != PersistedStepState.Applied)
            {
                return false;
            }
        }

        for (int index = sequence + 1; index < states.Count; index++)
        {
            if (states[index] != PersistedStepState.NotStarted)
            {
                return false;
            }
        }

        return true;
    }

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
            throw new JournalStorageException(
                ExecutionJournalPersistenceFailureKind.JournalFormatInvalid);
        }
    }

    private static JournalEnvelope Envelope(JournalPayload payload) =>
        new(payload, ComputeChecksum(payload));

    private static string ComputeChecksum(JournalPayload payload) =>
        Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions)));

    private static void WriteEnvelopeDurably(
        FileStream stream,
        JournalEnvelope envelope)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        stream.Write(json);
        stream.WriteByte((byte)'\n');
        stream.Flush(flushToDisk: true);
    }

    private static FileStream CreateJournalFile(
        SafeFileHandle parent,
        string fileName)
    {
        (SafeFileHandle handle, int status) = BackupNativeMethods.OpenRelative(
            parent,
            fileName,
            BackupNativeMethods.FileReadData |
            BackupNativeMethods.FileWriteData |
            BackupNativeMethods.FileReadAttributes |
            BackupNativeMethods.FileWriteAttributes |
            BackupNativeMethods.Synchronize,
            BackupNativeMethods.ShareRead,
            BackupNativeMethods.FileCreate,
            BackupNativeMethods.FileOpenReparsePoint |
            BackupNativeMethods.FileOpenNoRecall |
            BackupNativeMethods.FileSynchronousIoNonAlert);

        if (status != 0)
        {
            handle.Dispose();
            throw MapNativeFailure(status, missingIsInvalidReference: false);
        }

        ValidateRegularNoFollowFile(handle);

        return new FileStream(
            handle,
            FileAccess.ReadWrite,
            bufferSize: 4096,
            isAsync: false);
    }

    private static FileStream OpenJournalFile(
        ExecutionJournalReference journal,
        bool writable)
    {
        if (!TryNormalizeReference(
                journal,
                out string normalizedParent,
                out string fileName))
        {
            throw new JournalStorageException(
                ExecutionJournalPersistenceFailureKind.InvalidReference);
        }

        using HeldDirectory parent = OpenDirectoryChain(
            normalizedParent,
            writableFinal: false);

        uint desiredAccess =
            BackupNativeMethods.FileReadData |
            BackupNativeMethods.FileReadAttributes |
            BackupNativeMethods.Synchronize;
        if (writable)
        {
            desiredAccess |=
                BackupNativeMethods.FileWriteData |
                BackupNativeMethods.FileWriteAttributes;
        }

        (SafeFileHandle handle, int status) = BackupNativeMethods.OpenRelative(
            parent.Root,
            fileName,
            desiredAccess,
            BackupNativeMethods.ShareRead,
            BackupNativeMethods.FileOpen,
            BackupNativeMethods.FileOpenReparsePoint |
            BackupNativeMethods.FileOpenNoRecall |
            BackupNativeMethods.FileSynchronousIoNonAlert);

        if (status != 0)
        {
            handle.Dispose();
            throw MapNativeFailure(status, missingIsInvalidReference: true);
        }

        ValidateRegularNoFollowFile(handle);

        return new FileStream(
            handle,
            writable ? FileAccess.ReadWrite : FileAccess.Read,
            bufferSize: 4096,
            isAsync: false);
    }

    private static void ValidateRegularNoFollowFile(SafeFileHandle handle)
    {
        FileAttributes attributes = BackupNativeMethods.ReadAttributes(handle);
        if (attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            handle.Dispose();
            throw new JournalStorageException(
                ExecutionJournalPersistenceFailureKind.ReparsePoint);
        }

        if (attributes.HasFlag(FileAttributes.Directory))
        {
            handle.Dispose();
            throw new JournalStorageException(
                ExecutionJournalPersistenceFailureKind.InvalidReference);
        }
    }

    private static HeldDirectory OpenDirectoryChain(
        string path,
        bool writableFinal)
    {
        string driveRoot = path[..3];
        SafeFileHandle drive = BackupNativeMethods.CreateFileW(
            @"\\?\" + driveRoot,
            BackupNativeMethods.FileListDirectory |
            BackupNativeMethods.FileTraverse |
            BackupNativeMethods.FileReadAttributes |
            BackupNativeMethods.Synchronize,
            BackupNativeMethods.ShareRead,
            IntPtr.Zero,
            BackupNativeMethods.OpenExisting,
            BackupNativeMethods.BackupSemantics |
            BackupNativeMethods.OpenReparsePoint,
            IntPtr.Zero);

        if (drive.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            drive.Dispose();
            throw new JournalStorageException(error == 5
                ? ExecutionJournalPersistenceFailureKind.AccessDenied
                : ExecutionJournalPersistenceFailureKind.IoFailure);
        }

        var handles = new List<SafeFileHandle> { drive };
        try
        {
            string[] components = path[3..]
                .Split('\\', StringSplitOptions.RemoveEmptyEntries);

            for (int index = 0; index < components.Length; index++)
            {
                bool isFinal = index == components.Length - 1;
                uint desiredAccess =
                    BackupNativeMethods.FileListDirectory |
                    BackupNativeMethods.FileTraverse |
                    BackupNativeMethods.FileReadAttributes |
                    BackupNativeMethods.Synchronize;

                if (writableFinal && isFinal)
                {
                    desiredAccess |=
                        BackupNativeMethods.FileAddFile |
                        BackupNativeMethods.FileWriteAttributes;
                }

                (SafeFileHandle next, int status) = BackupNativeMethods.OpenRelative(
                    handles[^1],
                    components[index],
                    desiredAccess,
                    BackupNativeMethods.ShareRead,
                    BackupNativeMethods.FileOpen,
                    BackupNativeMethods.FileOpenReparsePoint |
                    BackupNativeMethods.FileOpenNoRecall |
                    BackupNativeMethods.FileSynchronousIoNonAlert);

                if (status != 0)
                {
                    next.Dispose();
                    throw MapNativeFailure(status, missingIsInvalidReference: false);
                }

                FileAttributes attributes = BackupNativeMethods.ReadAttributes(next);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    next.Dispose();
                    throw new JournalStorageException(
                        ExecutionJournalPersistenceFailureKind.ReparsePoint);
                }

                if (!attributes.HasFlag(FileAttributes.Directory))
                {
                    next.Dispose();
                    throw new JournalStorageException(
                        ExecutionJournalPersistenceFailureKind.InvalidPath);
                }

                handles.Add(next);
            }

            return new HeldDirectory(handles);
        }
        catch
        {
            foreach (SafeFileHandle handle in handles)
            {
                handle.Dispose();
            }

            throw;
        }
    }

    private static JournalStorageException MapNativeFailure(
        int status,
        bool missingIsInvalidReference)
    {
        if (status == BackupNativeMethods.StatusReparsePointEncountered)
        {
            return new JournalStorageException(
                ExecutionJournalPersistenceFailureKind.ReparsePoint);
        }

        uint error = BackupNativeMethods.RtlNtStatusToDosError(status);
        return new JournalStorageException(error switch
        {
            2 or 3 when missingIsInvalidReference =>
                ExecutionJournalPersistenceFailureKind.InvalidReference,
            2 or 3 or 123 or 161 =>
                ExecutionJournalPersistenceFailureKind.InvalidPath,
            5 => ExecutionJournalPersistenceFailureKind.AccessDenied,
            _ => ExecutionJournalPersistenceFailureKind.IoFailure,
        });
    }

    private static bool TryNormalizeReference(
        ExecutionJournalReference? journal,
        out string normalizedParent,
        out string fileName)
    {
        normalizedParent = "";
        fileName = "";

        if (journal is null ||
            !Guid.TryParseExact(journal.JournalId, "N", out _) ||
            string.IsNullOrWhiteSpace(journal.JournalPath))
        {
            return false;
        }

        int separator = journal.JournalPath.LastIndexOf('\\');
        if (separator <= 2 || separator == journal.JournalPath.Length - 1)
        {
            return false;
        }

        string parent = journal.JournalPath[..separator];
        fileName = journal.JournalPath[(separator + 1)..];
        string expectedName = $"mim-journal-{journal.JournalId}.jsonl";

        return string.Equals(fileName, expectedName, StringComparison.OrdinalIgnoreCase) &&
            TryNormalizeLocalDirectoryPath(parent, out normalizedParent);
    }

    private static bool TryNormalizeLocalDirectoryPath(
        string? path,
        out string normalized)
    {
        normalized = "";

        if (string.IsNullOrWhiteSpace(path) ||
            path.Length < 4 ||
            path.Length > 32000 ||
            !char.IsAsciiLetter(path[0]) ||
            path[1] != ':' ||
            path[2] != '\\')
        {
            return false;
        }

        string[] components = path[3..]
            .Split('\\', StringSplitOptions.RemoveEmptyEntries);

        if (components.Length == 0 ||
            components.Any(component => !IsSingleName(component)))
        {
            return false;
        }

        string driveRoot = path[..3];
        try
        {
            if (new DriveInfo(driveRoot).DriveType == DriveType.Network)
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        normalized = driveRoot + string.Join('\\', components);
        return true;
    }

    private static bool IsSingleName(string name) =>
        name.Length > 0 &&
        name is not "." and not ".." &&
        !name.EndsWith('.') &&
        !name.EndsWith(' ') &&
        name.All(character =>
            character >= 32 &&
            !"<>:".Contains(character) &&
            character != '"' &&
            character != '/' &&
            character != '\\' &&
            character != '|' &&
            character != '?' &&
            character != '*');

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character =>
            character is >= '0' and <= '9' or
            >= 'A' and <= 'F' or
            >= 'a' and <= 'f');

    private static ExecutionJournalWriteResult InvalidWrite(
        ExecutionJournalPersistenceFailureKind kind,
        ExecutionJournalReference? journal = null) =>
        new(
            ExecutionJournalWriteStatus.Invalid,
            journal,
            kind);

    private static ExecutionJournalReadResult InvalidRead(
        ExecutionJournalPersistenceFailureKind kind) =>
        new(
            ExecutionJournalReadStatus.Invalid,
            FailureKind: kind);

    private static ExecutionJournalWriteResult WriteFailure(
        ExecutionJournalPersistenceFailureKind kind,
        ExecutionJournalReference? journal) =>
        kind is
            ExecutionJournalPersistenceFailureKind.InvalidDraft or
            ExecutionJournalPersistenceFailureKind.InvalidReference or
            ExecutionJournalPersistenceFailureKind.InvalidPath or
            ExecutionJournalPersistenceFailureKind.ReparsePoint or
            ExecutionJournalPersistenceFailureKind.JournalFormatInvalid or
            ExecutionJournalPersistenceFailureKind.JournalStateInvalid
            ? InvalidWrite(kind, journal)
            : new ExecutionJournalWriteResult(
                ExecutionJournalWriteStatus.Failed,
                journal,
                kind);

    private static ExecutionJournalReadResult ReadFailure(
        ExecutionJournalPersistenceFailureKind kind) =>
        kind is
            ExecutionJournalPersistenceFailureKind.InvalidDraft or
            ExecutionJournalPersistenceFailureKind.InvalidReference or
            ExecutionJournalPersistenceFailureKind.InvalidPath or
            ExecutionJournalPersistenceFailureKind.ReparsePoint or
            ExecutionJournalPersistenceFailureKind.JournalFormatInvalid or
            ExecutionJournalPersistenceFailureKind.JournalStateInvalid
            ? InvalidRead(kind)
            : new ExecutionJournalReadResult(
                ExecutionJournalReadStatus.Failed,
                FailureKind: kind);

    private enum PersistedStepState
    {
        NotStarted,
        Started,
        Applied,
        Failed,
    }

    private sealed class HeldDirectory(List<SafeFileHandle> handles) : IDisposable
    {
        public SafeFileHandle Root => handles[^1];

        public void Dispose()
        {
            foreach (SafeFileHandle handle in handles)
            {
                handle.Dispose();
            }
        }
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
            new(
                fingerprint.FileCount,
                fingerprint.DirectoryCount,
                fingerprint.TotalBytes,
                fingerprint.Sha256);
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
        int NextIndex,
        string LastChecksum,
        int ValidLength,
        bool HasTornTail);
}
