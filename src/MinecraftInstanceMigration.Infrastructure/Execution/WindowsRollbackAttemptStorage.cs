using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Infrastructure.Backup;

namespace MinecraftInstanceMigration.Infrastructure.Execution;

public sealed class WindowsRollbackAttemptStorage : IRollbackAttemptStorage
{
    private const int MaximumJournalBytes = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public Task<RollbackAttemptWriteResult> CreateAsync(
        string journalParent,
        string destinationRoot,
        string? backupRoot,
        RollbackPlan plan,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => Create(
                journalParent,
                destinationRoot,
                backupRoot,
                plan,
                cancellationToken),
            CancellationToken.None);

    public Task<RollbackAttemptWriteResult> MarkActionStartedAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        int order,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => AppendTransition(
                attempt,
                plan,
                order,
                "Started",
                null,
                cancellationToken),
            CancellationToken.None);

    public Task<RollbackAttemptWriteResult> MarkActionAppliedAsync(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        int order,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => AppendTransition(
                attempt,
                plan,
                order,
                "Applied",
                null,
                cancellationToken),
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
            () => AppendTransition(
                attempt,
                plan,
                order,
                "Failed",
                failureKind.ToString(),
                cancellationToken),
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
            throw new PlatformNotSupportedException(
                "Rollback attempt storage requires Windows.");
        }

        if (!IsValidPlan(plan))
        {
            return InvalidWrite(
                RollbackAttemptPersistenceFailureKind.InvalidPlan);
        }

        if (!WindowsExecutionTree.TryNormalizeRoot(
                journalParent,
                out string journal) ||
            !WindowsExecutionTree.TryNormalizeRoot(
                destinationRoot,
                out string destination))
        {
            return InvalidWrite(
                RollbackAttemptPersistenceFailureKind.InvalidPath);
        }

        string? backup = null;
        if (!string.IsNullOrWhiteSpace(backupRoot) &&
            !WindowsExecutionTree.TryNormalizeRoot(
                backupRoot,
                out backup))
        {
            return InvalidWrite(
                RollbackAttemptPersistenceFailureKind.InvalidPath);
        }

        if (IsEqualOrDescendant(journal, destination) ||
            backup is not null &&
                IsEqualOrDescendant(journal, backup) ||
            backup is not null &&
                WindowsExecutionTree.RootsOverlap(
                    destination,
                    backup))
        {
            return InvalidWrite(
                RollbackAttemptPersistenceFailureKind.UnsafeWorkspace);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new RollbackAttemptWriteResult(
                RollbackAttemptWriteStatus.Cancelled);
        }

        RollbackAttemptReference? reference = null;

        try
        {
            using WindowsExecutionTree.HeldDirectory destinationDirectory =
                WindowsExecutionTree.OpenDirectoryChain(
                    destination,
                    writableFinal: false);
            using WindowsExecutionTree.HeldDirectory journalDirectory =
                WindowsExecutionTree.OpenDirectoryChain(
                    journal,
                    writableFinal: true);

            WindowsExecutionTree.HeldDirectory? backupDirectory = null;
            try
            {
                if (backup is not null)
                {
                    backupDirectory =
                        WindowsExecutionTree.OpenDirectoryChain(
                            backup,
                            writableFinal: false);
                }

                if (WindowsExecutionTree.IsPhysicalEqualOrDescendant(
                        journalDirectory.Root,
                        destinationDirectory.Root) ||
                    backupDirectory is not null &&
                        WindowsExecutionTree.IsPhysicalEqualOrDescendant(
                            journalDirectory.Root,
                            backupDirectory.Root) ||
                    backupDirectory is not null &&
                        WindowsExecutionTree.PhysicalRootsOverlap(
                            destinationDirectory,
                            backupDirectory))
                {
                    return InvalidWrite(
                        RollbackAttemptPersistenceFailureKind.UnsafeWorkspace);
                }

                cancellationToken.ThrowIfCancellationRequested();

                string attemptId = Guid.NewGuid().ToString("N");
                string fileName =
                    $"mim-rollback-{attemptId}.jsonl";
                reference = new RollbackAttemptReference(
                    attemptId,
                    Path.Combine(journal, fileName));

                using FileStream stream =
                    CreateJournalFile(
                        journalDirectory.Root,
                        fileName);

                JournalPayload payload = new(
                    Index: 0,
                    Kind: "Header",
                    PreviousChecksum: null,
                    SchemaVersion:
                        RollbackAttemptSnapshot.CurrentSchemaVersion,
                    AttemptId: attemptId,
                    Entries: plan.Entries
                        .Select(PersistedEntry.From)
                        .ToArray(),
                    Order: null,
                    FailureKind: null);

                WriteEnvelopeDurably(
                    stream,
                    Envelope(payload));

                return new RollbackAttemptWriteResult(
                    RollbackAttemptWriteStatus.Succeeded,
                    reference);
            }
            finally
            {
                backupDirectory?.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            return new RollbackAttemptWriteResult(
                RollbackAttemptWriteStatus.Cancelled,
                reference);
        }
        catch (RollbackAttemptStorageException error)
        {
            return WriteFailure(error.Kind, reference);
        }
        catch (ExecutionTreeException error)
        {
            return WriteFailure(
                MapTreeFailure(error.Kind),
                reference);
        }
        catch (UnauthorizedAccessException)
        {
            return WriteFailure(
                RollbackAttemptPersistenceFailureKind.AccessDenied,
                reference);
        }
        catch (System.ComponentModel.Win32Exception error)
            when (error.NativeErrorCode == 5)
        {
            return WriteFailure(
                RollbackAttemptPersistenceFailureKind.AccessDenied,
                reference);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return WriteFailure(
                RollbackAttemptPersistenceFailureKind.IoFailure,
                reference);
        }
        catch (IOException)
        {
            return WriteFailure(
                RollbackAttemptPersistenceFailureKind.IoFailure,
                reference);
        }
        catch (BackupNativeException error)
        {
            return WriteFailure(
                MapNativeFailure(error.Status),
                reference);
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
            throw new PlatformNotSupportedException(
                "Rollback attempt storage requires Windows.");
        }

        if (!IsValidPlan(plan) ||
            order < 0 ||
            order >= plan.Entries.Count ||
            plan.Entries[order].Order != order)
        {
            return InvalidWrite(
                RollbackAttemptPersistenceFailureKind.InvalidPlan,
                attempt);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new RollbackAttemptWriteResult(
                RollbackAttemptWriteStatus.Cancelled,
                attempt);
        }

        try
        {
            using FileStream stream =
                OpenJournalFile(attempt, writable: true);
            ParsedJournal parsed =
                ParseJournal(stream, attempt, plan);

            bool validTransition = kind switch
            {
                "Started" =>
                    CanStart(parsed.States, order),
                "Applied" or
                "GuardRejected" or
                "Failed" =>
                    parsed.States[order] ==
                    PersistedActionState.Started,
                _ => false,
            };

            bool validFailure = kind switch
            {
                "GuardRejected" or "Failed" =>
                    TryParseStorageFailure(
                        failureKind,
                        out _),
                _ => failureKind is null,
            };

            if (!validTransition || !validFailure)
            {
                return InvalidWrite(
                    RollbackAttemptPersistenceFailureKind
                        .JournalStateInvalid,
                    attempt);
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
                AttemptId: null,
                Entries: null,
                Order: order,
                FailureKind: failureKind);

            WriteEnvelopeDurably(
                stream,
                Envelope(payload));

            return new RollbackAttemptWriteResult(
                RollbackAttemptWriteStatus.Succeeded,
                attempt);
        }
        catch (OperationCanceledException)
        {
            return new RollbackAttemptWriteResult(
                RollbackAttemptWriteStatus.Cancelled,
                attempt);
        }
        catch (RollbackAttemptStorageException error)
        {
            return WriteFailure(error.Kind, attempt);
        }
        catch (ExecutionTreeException error)
        {
            return WriteFailure(
                MapTreeFailure(error.Kind),
                attempt);
        }
        catch (UnauthorizedAccessException)
        {
            return WriteFailure(
                RollbackAttemptPersistenceFailureKind.AccessDenied,
                attempt);
        }
        catch (System.ComponentModel.Win32Exception error)
            when (error.NativeErrorCode == 5)
        {
            return WriteFailure(
                RollbackAttemptPersistenceFailureKind.AccessDenied,
                attempt);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return WriteFailure(
                RollbackAttemptPersistenceFailureKind.IoFailure,
                attempt);
        }
        catch (IOException)
        {
            return WriteFailure(
                RollbackAttemptPersistenceFailureKind.IoFailure,
                attempt);
        }
        catch (BackupNativeException error)
        {
            return WriteFailure(
                MapNativeFailure(error.Status),
                attempt);
        }
    }

    private static RollbackAttemptReadResult Load(
        RollbackAttemptReference attempt,
        RollbackPlan plan,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Rollback attempt storage requires Windows.");
        }

        if (!IsValidPlan(plan))
        {
            return InvalidRead(
                RollbackAttemptPersistenceFailureKind.InvalidPlan);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new RollbackAttemptReadResult(
                RollbackAttemptReadStatus.Cancelled);
        }

        try
        {
            using FileStream stream =
                OpenJournalFile(attempt, writable: false);
            ParsedJournal parsed =
                ParseJournal(stream, attempt, plan);

            cancellationToken.ThrowIfCancellationRequested();

            return new RollbackAttemptReadResult(
                RollbackAttemptReadStatus.Loaded,
                parsed.Snapshot);
        }
        catch (OperationCanceledException)
        {
            return new RollbackAttemptReadResult(
                RollbackAttemptReadStatus.Cancelled);
        }
        catch (RollbackAttemptStorageException error)
        {
            return ReadFailure(error.Kind);
        }
        catch (ExecutionTreeException error)
        {
            return ReadFailure(
                MapTreeFailure(error.Kind));
        }
        catch (UnauthorizedAccessException)
        {
            return ReadFailure(
                RollbackAttemptPersistenceFailureKind.AccessDenied);
        }
        catch (System.ComponentModel.Win32Exception error)
            when (error.NativeErrorCode == 5)
        {
            return ReadFailure(
                RollbackAttemptPersistenceFailureKind.AccessDenied);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return ReadFailure(
                RollbackAttemptPersistenceFailureKind.IoFailure);
        }
        catch (IOException)
        {
            return ReadFailure(
                RollbackAttemptPersistenceFailureKind.IoFailure);
        }
        catch (BackupNativeException error)
        {
            return ReadFailure(
                MapNativeFailure(error.Status));
        }
    }

    private static ParsedJournal ParseJournal(
        FileStream stream,
        RollbackAttemptReference attempt,
        RollbackPlan plan)
    {
        if (stream.Length <= 0 ||
            stream.Length > MaximumJournalBytes)
        {
            throw InvalidJournal();
        }

        byte[] bytes =
            new byte[checked((int)stream.Length)];
        stream.Position = 0;
        int readOffset = 0;
        while (readOffset < bytes.Length)
        {
            int read = stream.Read(
                bytes,
                readOffset,
                bytes.Length - readOffset);

            if (read == 0)
            {
                throw InvalidJournal();
            }

            readOffset += read;
        }

        int lastNewline =
            Array.LastIndexOf(bytes, (byte)'\n');
        if (lastNewline < 0)
        {
            throw InvalidJournal();
        }

        int validLength = lastNewline + 1;
        bool hasTornTail =
            validLength != bytes.Length;

        var envelopes = new List<JournalEnvelope>();
        int start = 0;

        while (start < validLength)
        {
            int newline = Array.IndexOf(
                bytes,
                (byte)'\n',
                start,
                validLength - start);

            if (newline < 0)
            {
                throw InvalidJournal();
            }

            int length = newline - start;
            if (length > 0 &&
                bytes[newline - 1] == (byte)'\r')
            {
                length--;
            }

            if (length <= 0)
            {
                throw InvalidJournal();
            }

            JournalEnvelope? envelope;
            try
            {
                envelope =
                    JsonSerializer.Deserialize<JournalEnvelope>(
                        bytes.AsSpan(start, length),
                        JsonOptions);
            }
            catch (JsonException)
            {
                throw InvalidJournal();
            }

            if (envelope?.Payload is null ||
                !IsSha256(envelope.Checksum) ||
                !string.Equals(
                    envelope.Checksum,
                    ComputeChecksum(envelope.Payload),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw InvalidJournal();
            }

            envelopes.Add(envelope);
            start = newline + 1;
        }

        if (envelopes.Count == 0)
        {
            throw InvalidJournal();
        }

        ValidateHeader(
            envelopes[0],
            attempt,
            plan);

        var states = Enumerable
            .Repeat(
                PersistedActionState.NotStarted,
                plan.Entries.Count)
            .ToArray();
        var failures =
            new RollbackStorageFailureKind?[
                plan.Entries.Count];

        string previousChecksum =
            envelopes[0].Checksum;

        for (int index = 1;
            index < envelopes.Count;
            index++)
        {
            JournalEnvelope envelope = envelopes[index];
            JournalPayload payload = envelope.Payload;

            if (payload.Index != index ||
                !string.Equals(
                    payload.PreviousChecksum,
                    previousChecksum,
                    StringComparison.OrdinalIgnoreCase) ||
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
                    if (payload.FailureKind is not null ||
                        !CanStart(states, order))
                    {
                        throw InvalidState();
                    }

                    states[order] =
                        PersistedActionState.Started;
                    break;

                case "Applied":
                    if (payload.FailureKind is not null ||
                        states[order] !=
                            PersistedActionState.Started)
                    {
                        throw InvalidState();
                    }

                    states[order] =
                        PersistedActionState.Applied;
                    break;

                case "GuardRejected":
                    if (states[order] !=
                            PersistedActionState.Started ||
                        !TryParseStorageFailure(
                            payload.FailureKind,
                            out RollbackStorageFailureKind
                                rejectedKind))
                    {
                        throw InvalidState();
                    }

                    states[order] =
                        PersistedActionState.GuardRejected;
                    failures[order] = rejectedKind;
                    break;

                case "Failed":
                    if (states[order] !=
                            PersistedActionState.Started ||
                        !TryParseStorageFailure(
                            payload.FailureKind,
                            out RollbackStorageFailureKind
                                failedKind))
                    {
                        throw InvalidState();
                    }

                    states[order] =
                        PersistedActionState.Failed;
                    failures[order] = failedKind;
                    break;

                default:
                    throw InvalidJournal();
            }

            previousChecksum = envelope.Checksum;
        }

        var steps =
            new RollbackAttemptStep[plan.Entries.Count];

        for (int index = 0;
            index < plan.Entries.Count;
            index++)
        {
            RollbackPlanEntry entry =
                plan.Entries[index];

            RollbackAttemptStepOutcome outcome =
                states[index] switch
                {
                    PersistedActionState.NotStarted =>
                        RollbackAttemptStepOutcome.NotStarted,
                    PersistedActionState.Started =>
                        RollbackAttemptStepOutcome.Uncertain,
                    PersistedActionState.Applied =>
                        RollbackAttemptStepOutcome.Applied,
                    PersistedActionState.GuardRejected =>
                        RollbackAttemptStepOutcome.GuardRejected,
                    PersistedActionState.Failed =>
                        RollbackAttemptStepOutcome.Failed,
                    _ => throw InvalidState(),
                };

            steps[index] = new RollbackAttemptStep(
                entry.Order,
                entry.Name,
                entry.Action,
                outcome,
                failures[index]);
        }

        return new ParsedJournal(
            new RollbackAttemptSnapshot(
                RollbackAttemptSnapshot.CurrentSchemaVersion,
                steps),
            states,
            envelopes.Count,
            previousChecksum,
            validLength,
            hasTornTail);
    }

    private static void ValidateHeader(
        JournalEnvelope header,
        RollbackAttemptReference attempt,
        RollbackPlan plan)
    {
        JournalPayload payload = header.Payload;

        if (payload.Index != 0 ||
            !string.Equals(
                payload.Kind,
                "Header",
                StringComparison.Ordinal) ||
            payload.PreviousChecksum is not null ||
            payload.SchemaVersion !=
                RollbackAttemptSnapshot.CurrentSchemaVersion ||
            !string.Equals(
                payload.AttemptId,
                attempt.AttemptId,
                StringComparison.Ordinal) ||
            payload.Entries is null ||
            payload.Entries.Count !=
                plan.Entries.Count ||
            payload.Order is not null ||
            payload.FailureKind is not null)
        {
            throw InvalidJournal();
        }

        for (int index = 0;
            index < plan.Entries.Count;
            index++)
        {
            PersistedEntry actual =
                payload.Entries[index];
            RollbackPlanEntry expected =
                plan.Entries[index];

            if (actual.Order != index ||
                expected.Order != index ||
                !string.Equals(
                    actual.Name,
                    expected.Name,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    actual.ExpectedKind,
                    expected.ExpectedKind.ToString(),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    actual.Operation,
                    expected.Operation.ToString(),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    actual.Action,
                    expected.Action.ToString(),
                    StringComparison.Ordinal) ||
                actual.Fingerprint is null ||
                !actual.Fingerprint.Equals(
                    PersistedFingerprint.From(
                        expected.ExpectedCurrentFingerprint!)))
            {
                throw InvalidJournal();
            }
        }
    }

    private static bool IsValidPlan(
        RollbackPlan? plan)
    {
        if (plan is null ||
            !plan.CanAttemptAutomaticRollback)
        {
            return false;
        }

        for (int index = 0;
            index < plan.Entries.Count;
            index++)
        {
            RollbackPlanEntry entry =
                plan.Entries[index];

            if (entry.Order != index ||
                entry.ExpectedCurrentFingerprint is null ||
                entry.RecoveryReason is not null)
            {
                return false;
            }

            bool validAction =
                entry.Action switch
                {
                    RollbackActionKind.DeleteCreatedEntry =>
                        entry.Operation ==
                        ExecutionOperationKind.Copy,
                    RollbackActionKind.RestoreFromBackup =>
                        entry.Operation ==
                        ExecutionOperationKind.Replace,
                    _ => false,
                };

            if (!validAction)
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanStart(
        IReadOnlyList<PersistedActionState> states,
        int order)
    {
        if (states[order] !=
            PersistedActionState.NotStarted)
        {
            return false;
        }

        for (int index = 0;
            index < order;
            index++)
        {
            if (states[index] !=
                PersistedActionState.Applied)
            {
                return false;
            }
        }

        for (int index = order + 1;
            index < states.Count;
            index++)
        {
            if (states[index] !=
                PersistedActionState.NotStarted)
            {
                return false;
            }
        }

        return true;
    }

    private static FileStream CreateJournalFile(
        SafeFileHandle parent,
        string fileName)
    {
        (SafeFileHandle handle, int status) =
            BackupNativeMethods.OpenRelative(
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
            throw MapNativeException(status);
        }

        ValidateRegularFile(handle);

        return new FileStream(
            handle,
            FileAccess.ReadWrite,
            4096,
            isAsync: false);
    }

    private static FileStream OpenJournalFile(
        RollbackAttemptReference attempt,
        bool writable)
    {
        if (!TryNormalizeReference(
                attempt,
                out string parent,
                out string fileName))
        {
            throw new RollbackAttemptStorageException(
                RollbackAttemptPersistenceFailureKind
                    .InvalidReference);
        }

        using WindowsExecutionTree.HeldDirectory
            directory =
                WindowsExecutionTree.OpenDirectoryChain(
                    parent,
                    writableFinal: false);

        uint access =
            BackupNativeMethods.FileReadData |
            BackupNativeMethods.FileReadAttributes |
            BackupNativeMethods.Synchronize;

        if (writable)
        {
            access |=
                BackupNativeMethods.FileWriteData |
                BackupNativeMethods.FileWriteAttributes;
        }

        (SafeFileHandle handle, int status) =
            BackupNativeMethods.OpenRelative(
                directory.Root,
                fileName,
                access,
                BackupNativeMethods.ShareRead,
                BackupNativeMethods.FileOpen,
                BackupNativeMethods.FileOpenReparsePoint |
                BackupNativeMethods.FileOpenNoRecall |
                BackupNativeMethods.FileSynchronousIoNonAlert);

        if (status != 0)
        {
            handle.Dispose();
            throw MapNativeException(status);
        }

        ValidateRegularFile(handle);

        return new FileStream(
            handle,
            writable
                ? FileAccess.ReadWrite
                : FileAccess.Read,
            4096,
            isAsync: false);
    }

    private static void ValidateRegularFile(
        SafeFileHandle handle)
    {
        FileAttributes attributes =
            BackupNativeMethods.ReadAttributes(handle);

        if (attributes.HasFlag(
                FileAttributes.ReparsePoint))
        {
            handle.Dispose();
            throw new RollbackAttemptStorageException(
                RollbackAttemptPersistenceFailureKind
                    .ReparsePoint);
        }

        if (attributes.HasFlag(
                FileAttributes.Directory))
        {
            handle.Dispose();
            throw new RollbackAttemptStorageException(
                RollbackAttemptPersistenceFailureKind
                    .InvalidReference);
        }
    }

    private static void WriteEnvelopeDurably(
        FileStream stream,
        JournalEnvelope envelope)
    {
        byte[] json =
            JsonSerializer.SerializeToUtf8Bytes(
                envelope,
                JsonOptions);

        stream.Write(json);
        stream.WriteByte((byte)'\n');
        stream.Flush(flushToDisk: true);
    }

    private static JournalEnvelope Envelope(
        JournalPayload payload) =>
        new(
            payload,
            ComputeChecksum(payload));

    private static string ComputeChecksum(
        JournalPayload payload) =>
        Convert.ToHexString(
            SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(
                    payload,
                    JsonOptions)));

    private static bool TryNormalizeReference(
        RollbackAttemptReference? attempt,
        out string parent,
        out string fileName)
    {
        parent = "";
        fileName = "";

        if (attempt is null ||
            !Guid.TryParseExact(
                attempt.AttemptId,
                "N",
                out _) ||
            string.IsNullOrWhiteSpace(
                attempt.JournalPath))
        {
            return false;
        }

        int separator =
            attempt.JournalPath.LastIndexOf('\\');

        if (separator <= 2 ||
            separator ==
                attempt.JournalPath.Length - 1)
        {
            return false;
        }

        string candidateParent =
            attempt.JournalPath[..separator];
        fileName =
            attempt.JournalPath[(separator + 1)..];

        string expectedName =
            $"mim-rollback-{attempt.AttemptId}.jsonl";

        return string.Equals(
                fileName,
                expectedName,
                StringComparison.OrdinalIgnoreCase) &&
            WindowsExecutionTree.TryNormalizeRoot(
                candidateParent,
                out parent);
    }

    private static bool IsEqualOrDescendant(
        string candidate,
        string root) =>
        string.Equals(
            candidate,
            root,
            StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith(
            root.TrimEnd('\\') + "\\",
            StringComparison.OrdinalIgnoreCase);

    private static bool TryParseStorageFailure(
        string? value,
        out RollbackStorageFailureKind kind) =>
        Enum.TryParse(
            value,
            ignoreCase: false,
            out kind) &&
        Enum.IsDefined(kind);

    private static bool IsSha256(
        string? value) =>
        value is { Length: 64 } &&
        value.All(character =>
            character is >= '0' and <= '9' or
            >= 'A' and <= 'F' or
            >= 'a' and <= 'f');

    private static RollbackAttemptWriteResult
        InvalidWrite(
            RollbackAttemptPersistenceFailureKind kind,
            RollbackAttemptReference? attempt = null) =>
        new(
            RollbackAttemptWriteStatus.Invalid,
            attempt,
            kind);

    private static RollbackAttemptReadResult
        InvalidRead(
            RollbackAttemptPersistenceFailureKind kind) =>
        new(
            RollbackAttemptReadStatus.Invalid,
            FailureKind: kind);

    private static RollbackAttemptWriteResult
        WriteFailure(
            RollbackAttemptPersistenceFailureKind kind,
            RollbackAttemptReference? attempt) =>
        kind is
            RollbackAttemptPersistenceFailureKind.InvalidPlan or
            RollbackAttemptPersistenceFailureKind.InvalidReference or
            RollbackAttemptPersistenceFailureKind.InvalidPath or
            RollbackAttemptPersistenceFailureKind.UnsafeWorkspace or
            RollbackAttemptPersistenceFailureKind.ReparsePoint or
            RollbackAttemptPersistenceFailureKind.JournalFormatInvalid or
            RollbackAttemptPersistenceFailureKind.JournalStateInvalid
            ? InvalidWrite(kind, attempt)
            : new RollbackAttemptWriteResult(
                RollbackAttemptWriteStatus.Failed,
                attempt,
                kind);

    private static RollbackAttemptReadResult
        ReadFailure(
            RollbackAttemptPersistenceFailureKind kind) =>
        kind is
            RollbackAttemptPersistenceFailureKind.InvalidPlan or
            RollbackAttemptPersistenceFailureKind.InvalidReference or
            RollbackAttemptPersistenceFailureKind.InvalidPath or
            RollbackAttemptPersistenceFailureKind.UnsafeWorkspace or
            RollbackAttemptPersistenceFailureKind.ReparsePoint or
            RollbackAttemptPersistenceFailureKind.JournalFormatInvalid or
            RollbackAttemptPersistenceFailureKind.JournalStateInvalid
            ? InvalidRead(kind)
            : new RollbackAttemptReadResult(
                RollbackAttemptReadStatus.Failed,
                FailureKind: kind);

    private static RollbackAttemptPersistenceFailureKind
        MapTreeFailure(ExecutionTreeFailureKind kind) =>
        kind switch
        {
            ExecutionTreeFailureKind.InvalidPath =>
                RollbackAttemptPersistenceFailureKind.InvalidPath,
            ExecutionTreeFailureKind.Missing or
            ExecutionTreeFailureKind.Changed =>
                RollbackAttemptPersistenceFailureKind.InvalidReference,
            ExecutionTreeFailureKind.ReparsePoint =>
                RollbackAttemptPersistenceFailureKind.ReparsePoint,
            ExecutionTreeFailureKind.AccessDenied =>
                RollbackAttemptPersistenceFailureKind.AccessDenied,
            _ =>
                RollbackAttemptPersistenceFailureKind.IoFailure,
        };

    private static RollbackAttemptPersistenceFailureKind
        MapNativeFailure(int status)
    {
        if (status ==
            BackupNativeMethods.StatusReparsePointEncountered)
        {
            return RollbackAttemptPersistenceFailureKind
                .ReparsePoint;
        }

        uint error =
            BackupNativeMethods.RtlNtStatusToDosError(
                status);

        return error switch
        {
            2 or 3 =>
                RollbackAttemptPersistenceFailureKind
                    .InvalidReference,
            5 =>
                RollbackAttemptPersistenceFailureKind
                    .AccessDenied,
            123 or 161 or 206 =>
                RollbackAttemptPersistenceFailureKind
                    .InvalidPath,
            _ =>
                RollbackAttemptPersistenceFailureKind
                    .IoFailure,
        };
    }

    private static RollbackAttemptStorageException
        MapNativeException(int status) =>
        new(MapNativeFailure(status));

    private static RollbackAttemptStorageException
        InvalidJournal() =>
        new(
            RollbackAttemptPersistenceFailureKind
                .JournalFormatInvalid);

    private static RollbackAttemptStorageException
        InvalidState() =>
        new(
            RollbackAttemptPersistenceFailureKind
                .JournalStateInvalid);

    private enum PersistedActionState
    {
        NotStarted,
        Started,
        Applied,
        GuardRejected,
        Failed,
    }

    private sealed class RollbackAttemptStorageException(
        RollbackAttemptPersistenceFailureKind kind)
        : Exception
    {
        internal RollbackAttemptPersistenceFailureKind Kind { get; } = kind;
    }

    private sealed record PersistedFingerprint(
        int FileCount,
        int DirectoryCount,
        long TotalBytes,
        string Sha256)
    {
        internal static PersistedFingerprint From(
            ExecutionContentFingerprint fingerprint) =>
            new(
                fingerprint.FileCount,
                fingerprint.DirectoryCount,
                fingerprint.TotalBytes,
                fingerprint.Sha256);
    }

    private sealed record PersistedEntry(
        int Order,
        string Name,
        string ExpectedKind,
        string Operation,
        string Action,
        PersistedFingerprint? Fingerprint)
    {
        internal static PersistedEntry From(
            RollbackPlanEntry entry) =>
            new(
                entry.Order,
                entry.Name,
                entry.ExpectedKind.ToString(),
                entry.Operation.ToString(),
                entry.Action.ToString(),
                entry.ExpectedCurrentFingerprint is null
                    ? null
                    : PersistedFingerprint.From(
                        entry.ExpectedCurrentFingerprint));
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
        int NextIndex,
        string LastChecksum,
        int ValidLength,
        bool HasTornTail);
}
