using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Inspection;

namespace MinecraftInstanceMigration.Infrastructure.Backup;

public sealed class WindowsBackupStorage : IBackupStorage
{
    private const int BufferSize = 128 * 1024;

    public Task<BackupExecutionResult> CreateBackupAsync(
        string destinationRoot,
        string backupParent,
        BackupPlan plan,
        BackupManifestDraft manifest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(manifest);

        return Task.Run(
            () => CreateBackup(destinationRoot, backupParent, plan, manifest, cancellationToken),
            CancellationToken.None);
    }

    private static BackupExecutionResult CreateBackup(
        string destinationRoot,
        string backupParent,
        BackupPlan plan,
        BackupManifestDraft manifest,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Backup storage requires Windows.");
        }

        if (!ManifestMatchesPlan(plan, manifest))
        {
            return new BackupExecutionResult(
                BackupExecutionStatus.Failed,
                FailureKind: BackupFailureKind.InvalidPlan);
        }

        if (!TryNormalizeLocalDirectoryPath(destinationRoot, out string normalizedDestination) ||
            !TryNormalizeLocalDirectoryPath(backupParent, out string normalizedBackupParent))
        {
            return new BackupExecutionResult(
                BackupExecutionStatus.Failed,
                FailureKind: BackupFailureKind.InvalidPath);
        }

        if (IsEqualOrDescendant(normalizedBackupParent, normalizedDestination))
        {
            return new BackupExecutionResult(
                BackupExecutionStatus.Failed,
                FailureKind: BackupFailureKind.OverlappingRoots);
        }

        string? backupRootPath = null;
        int entriesCopied = 0;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using HeldDirectory destination = OpenDirectoryChain(normalizedDestination, writableFinal: false);
            using HeldDirectory backupParentDirectory = OpenDirectoryChain(normalizedBackupParent, writableFinal: true);

            string executionId = Guid.NewGuid().ToString("N");
            string backupRootName = $"mim-backup-{executionId}";
            backupRootPath = Path.Combine(normalizedBackupParent, backupRootName);

            if (IsEqualOrDescendant(backupRootPath, normalizedDestination))
            {
                return new BackupExecutionResult(
                    BackupExecutionStatus.Failed,
                    FailureKind: BackupFailureKind.OverlappingRoots);
            }

            using SafeFileHandle backupRoot = CreateDirectory(backupParentDirectory.Root, backupRootName);
            WriteOwnedFile(
                backupRoot,
                ".mim-backup-owner.json",
                JsonSerializer.SerializeToUtf8Bytes(new OwnerMarker(1, executionId)),
                cancellationToken);

            foreach (BackupPlanEntry entry in plan.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateSingleName(entry.Name);

                using OpenedNode source = OpenExistingNode(destination.Root, entry.Name);
                bool expectedDirectory = entry.DestinationState == EntryState.Directory;
                if (source.IsDirectory != expectedDirectory)
                {
                    throw new BackupStorageException(BackupFailureKind.SourceChanged);
                }

                CopyNode(source, backupRoot, entry.Name, cancellationToken);
                entriesCopied++;
            }

            TreeFingerprint sourceFirst = FingerprintPlan(destination.Root, plan, cancellationToken);
            TreeFingerprint backup = FingerprintPlan(backupRoot, plan, cancellationToken);
            TreeFingerprint sourceSecond = FingerprintPlan(destination.Root, plan, cancellationToken);

            if (!sourceFirst.Equals(sourceSecond))
            {
                throw new BackupStorageException(BackupFailureKind.SourceChanged);
            }

            if (!sourceSecond.Equals(backup))
            {
                throw new BackupStorageException(BackupFailureKind.VerificationFailed);
            }

            var verification = new BackupVerificationSummary(
                backup.FileCount,
                backup.DirectoryCount,
                backup.TotalBytes,
                backup.Sha256);

            byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(
                new CompletedManifest(
                    manifest.SchemaVersion,
                    manifest.Entries.Select(entry => new ManifestEntry(
                        entry.Name,
                        entry.ExpectedKind.ToString(),
                        entry.DestinationState.ToString())).ToArray(),
                    verification),
                new JsonSerializerOptions { WriteIndented = true });

            WriteOwnedFile(backupRoot, "backup-manifest.json", manifestBytes, cancellationToken);

            return new BackupExecutionResult(
                BackupExecutionStatus.Completed,
                backupRootPath,
                EntriesCopied: entriesCopied,
                Verification: verification);
        }
        catch (OperationCanceledException)
        {
            return new BackupExecutionResult(
                BackupExecutionStatus.Cancelled,
                backupRootPath,
                EntriesCopied: entriesCopied);
        }
        catch (BackupStorageException error)
        {
            return new BackupExecutionResult(
                BackupExecutionStatus.Failed,
                backupRootPath,
                error.Kind,
                entriesCopied);
        }
        catch (UnauthorizedAccessException)
        {
            return new BackupExecutionResult(
                BackupExecutionStatus.Failed,
                backupRootPath,
                BackupFailureKind.AccessDenied,
                entriesCopied);
        }
        catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 5)
        {
            return new BackupExecutionResult(
                BackupExecutionStatus.Failed,
                backupRootPath,
                BackupFailureKind.AccessDenied,
                entriesCopied);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new BackupExecutionResult(
                BackupExecutionStatus.Failed,
                backupRootPath,
                BackupFailureKind.IoFailure,
                entriesCopied);
        }
        catch (IOException)
        {
            return new BackupExecutionResult(
                BackupExecutionStatus.Failed,
                backupRootPath,
                BackupFailureKind.IoFailure,
                entriesCopied);
        }
        catch (BackupNativeException error)
        {
            BackupFailureKind kind = error.Status == BackupNativeMethods.StatusReparsePointEncountered
                ? BackupFailureKind.ReparsePoint
                : BackupNativeMethods.RtlNtStatusToDosError(error.Status) == 5
                    ? BackupFailureKind.AccessDenied
                    : BackupFailureKind.IoFailure;
            return new BackupExecutionResult(
                BackupExecutionStatus.Failed,
                backupRootPath,
                kind,
                entriesCopied);
        }
    }

    private static bool ManifestMatchesPlan(BackupPlan plan, BackupManifestDraft manifest)
    {
        if (!plan.CanStartBackup ||
            manifest.SchemaVersion != BackupManifestDraft.CurrentSchemaVersion ||
            plan.Entries.Count != manifest.Entries.Count)
        {
            return false;
        }

        for (int index = 0; index < plan.Entries.Count; index++)
        {
            BackupPlanEntry planEntry = plan.Entries[index];
            BackupManifestEntryDraft manifestEntry = manifest.Entries[index];
            if (!string.Equals(planEntry.Name, manifestEntry.Name, StringComparison.Ordinal) ||
                planEntry.ExpectedKind != manifestEntry.ExpectedKind ||
                planEntry.DestinationState != manifestEntry.DestinationState)
            {
                return false;
            }
        }

        return true;
    }

    private static void CopyNode(
        OpenedNode source,
        SafeFileHandle backupParent,
        string name,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (source.IsDirectory)
        {
            using SafeFileHandle destination = CreateDirectory(backupParent, name);
            foreach (string childName in BackupNativeMethods.EnumerateNames(source.Handle))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateSingleName(childName);
                using OpenedNode child = OpenExistingNode(source.Handle, childName);
                CopyNode(child, destination, childName, cancellationToken);
            }

            return;
        }

        using SafeFileHandle destinationFile = CreateFile(backupParent, name);
        CopyFileBytes(source.Handle, destinationFile, cancellationToken);
    }

    private static void CopyFileBytes(
        SafeFileHandle source,
        SafeFileHandle destination,
        CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            long offset = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = RandomAccess.Read(source, buffer.AsSpan(0, BufferSize), offset);
                if (read == 0)
                {
                    break;
                }

                RandomAccess.Write(destination, buffer.AsSpan(0, read), offset);
                offset += read;
            }

            RandomAccess.FlushToDisk(destination);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
        }
    }

    private static TreeFingerprint FingerprintPlan(
        SafeFileHandle root,
        BackupPlan plan,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int files = 0;
        int directories = 0;
        long totalBytes = 0;

        foreach (BackupPlanEntry entry in plan.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using OpenedNode node = OpenExistingNode(root, entry.Name);
            AppendNodeFingerprint(
                node,
                entry.Name,
                hash,
                ref files,
                ref directories,
                ref totalBytes,
                cancellationToken);
        }

        return new TreeFingerprint(
            files,
            directories,
            totalBytes,
            Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static void AppendNodeFingerprint(
        OpenedNode node,
        string relativePath,
        IncrementalHash hash,
        ref int files,
        ref int directories,
        ref long totalBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppendRecordPrefix(hash, node.IsDirectory ? (byte)1 : (byte)2, relativePath);

        if (node.IsDirectory)
        {
            directories++;
            foreach (string childName in BackupNativeMethods.EnumerateNames(node.Handle))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateSingleName(childName);
                using OpenedNode child = OpenExistingNode(node.Handle, childName);
                AppendNodeFingerprint(
                    child,
                    relativePath + "/" + childName,
                    hash,
                    ref files,
                    ref directories,
                    ref totalBytes,
                    cancellationToken);
            }

            return;
        }

        files++;
        long length = RandomAccess.GetLength(node.Handle);
        totalBytes = checked(totalBytes + length);
        Span<byte> lengthBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(lengthBytes, length);
        hash.AppendData(lengthBytes);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            long offset = 0;
            while (offset < length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = RandomAccess.Read(node.Handle, buffer.AsSpan(0, BufferSize), offset);
                if (read == 0)
                {
                    throw new BackupStorageException(BackupFailureKind.SourceChanged);
                }

                hash.AppendData(buffer.AsSpan(0, read));
                offset += read;
            }

            if (RandomAccess.GetLength(node.Handle) != length)
            {
                throw new BackupStorageException(BackupFailureKind.SourceChanged);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
        }
    }

    private static void AppendRecordPrefix(IncrementalHash hash, byte type, string relativePath)
    {
        Span<byte> typeBytes = stackalloc byte[1];
        typeBytes[0] = type;
        hash.AppendData(typeBytes);

        byte[] path = Encoding.UTF8.GetBytes(relativePath);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, path.Length);
        hash.AppendData(length);
        hash.AppendData(path);
    }

    private static OpenedNode OpenExistingNode(SafeFileHandle parent, string name)
    {
        ValidateSingleName(name);

        (SafeFileHandle metadata, int metadataStatus) = BackupNativeMethods.OpenRelative(
            parent,
            name,
            BackupNativeMethods.FileReadAttributes | BackupNativeMethods.Synchronize,
            BackupNativeMethods.ShareRead,
            BackupNativeMethods.FileOpen,
            BackupNativeMethods.FileOpenReparsePoint |
            BackupNativeMethods.FileOpenNoRecall |
            BackupNativeMethods.FileSynchronousIoNonAlert);

        if (metadataStatus != 0)
        {
            metadata.Dispose();
            throw MapNativeFailure(metadataStatus);
        }

        using (metadata)
        {
            FileAttributes attributes = BackupNativeMethods.ReadAttributes(metadata);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new BackupStorageException(BackupFailureKind.ReparsePoint);
            }

            bool directory = attributes.HasFlag(FileAttributes.Directory);
            uint desiredAccess = (directory
                ? BackupNativeMethods.FileListDirectory | BackupNativeMethods.FileTraverse
                : BackupNativeMethods.FileReadData) |
                BackupNativeMethods.FileReadAttributes |
                BackupNativeMethods.Synchronize;
            uint options = BackupNativeMethods.FileOpenReparsePoint |
                BackupNativeMethods.FileOpenNoRecall |
                BackupNativeMethods.FileSynchronousIoNonAlert;

            (SafeFileHandle data, int status) = BackupNativeMethods.OpenRelative(
                parent,
                name,
                desiredAccess,
                BackupNativeMethods.ShareRead,
                BackupNativeMethods.FileOpen,
                options);
            if (status != 0)
            {
                data.Dispose();
                throw MapNativeFailure(status);
            }

            FileAttributes confirmed = BackupNativeMethods.ReadAttributes(data);
            if (confirmed.HasFlag(FileAttributes.ReparsePoint))
            {
                data.Dispose();
                throw new BackupStorageException(BackupFailureKind.ReparsePoint);
            }

            bool confirmedDirectory = confirmed.HasFlag(FileAttributes.Directory);
            if (confirmedDirectory != directory)
            {
                data.Dispose();
                throw new BackupStorageException(BackupFailureKind.SourceChanged);
            }

            return new OpenedNode(data, directory);
        }
    }

    private static SafeFileHandle CreateDirectory(SafeFileHandle parent, string name)
    {
        ValidateSingleName(name);
        (SafeFileHandle handle, int status) = BackupNativeMethods.OpenRelative(
            parent,
            name,
            BackupNativeMethods.FileListDirectory |
            BackupNativeMethods.FileAddFile |
            BackupNativeMethods.FileAddSubdirectory |
            BackupNativeMethods.FileTraverse |
            BackupNativeMethods.FileReadAttributes |
            BackupNativeMethods.FileWriteAttributes |
            BackupNativeMethods.Synchronize,
            BackupNativeMethods.ShareRead,
            BackupNativeMethods.FileCreate,
            BackupNativeMethods.FileDirectoryFile |
            BackupNativeMethods.FileSynchronousIoNonAlert);
        if (status != 0)
        {
            handle.Dispose();
            throw MapNativeFailure(status);
        }

        return handle;
    }

    private static SafeFileHandle CreateFile(SafeFileHandle parent, string name)
    {
        ValidateSingleName(name);
        (SafeFileHandle handle, int status) = BackupNativeMethods.OpenRelative(
            parent,
            name,
            BackupNativeMethods.FileReadData |
            BackupNativeMethods.FileWriteData |
            BackupNativeMethods.FileReadAttributes |
            BackupNativeMethods.FileWriteAttributes |
            BackupNativeMethods.Synchronize,
            BackupNativeMethods.ShareRead,
            BackupNativeMethods.FileCreate,
            BackupNativeMethods.FileNonDirectoryFile |
            BackupNativeMethods.FileSynchronousIoNonAlert);
        if (status != 0)
        {
            handle.Dispose();
            throw MapNativeFailure(status);
        }

        return handle;
    }

    private static void WriteOwnedFile(
        SafeFileHandle parent,
        string name,
        byte[] content,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SafeFileHandle file = CreateFile(parent, name);
        if (content.Length > 0)
        {
            RandomAccess.Write(file, content, 0);
        }

        RandomAccess.FlushToDisk(file);
    }

    private static HeldDirectory OpenDirectoryChain(string path, bool writableFinal)
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
            BackupNativeMethods.BackupSemantics | BackupNativeMethods.OpenReparsePoint,
            IntPtr.Zero);
        if (drive.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            drive.Dispose();
            throw new BackupStorageException(error == 5
                ? BackupFailureKind.AccessDenied
                : BackupFailureKind.IoFailure);
        }

        var handles = new List<SafeFileHandle> { drive };
        try
        {
            string[] components = path[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
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
                    desiredAccess |= BackupNativeMethods.FileAddFile |
                        BackupNativeMethods.FileAddSubdirectory |
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
                    throw MapRootFailure(status);
                }

                FileAttributes attributes = BackupNativeMethods.ReadAttributes(next);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    next.Dispose();
                    throw new BackupStorageException(BackupFailureKind.ReparsePoint);
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

    private static BackupStorageException MapNativeFailure(int status)
    {
        if (status == BackupNativeMethods.StatusReparsePointEncountered)
        {
            return new BackupStorageException(BackupFailureKind.ReparsePoint);
        }

        uint error = BackupNativeMethods.RtlNtStatusToDosError(status);
        return new BackupStorageException(error switch
        {
            2 or 3 => BackupFailureKind.SourceChanged,
            5 => BackupFailureKind.AccessDenied,
            _ => BackupFailureKind.IoFailure,
        });
    }

    private static BackupStorageException MapRootFailure(int status)
    {
        if (status == BackupNativeMethods.StatusReparsePointEncountered)
        {
            return new BackupStorageException(BackupFailureKind.ReparsePoint);
        }

        uint error = BackupNativeMethods.RtlNtStatusToDosError(status);
        return new BackupStorageException(error switch
        {
            2 or 3 or 123 or 161 => BackupFailureKind.InvalidPath,
            5 => BackupFailureKind.AccessDenied,
            _ => BackupFailureKind.IoFailure,
        });
    }

    private static bool TryNormalizeLocalDirectoryPath(string? path, out string normalized)
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

        string[] components = path[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (components.Length == 0 || components.Any(component => !IsSingleName(component)))
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

    private static bool IsEqualOrDescendant(string candidate, string root)
    {
        if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return candidate.StartsWith(
            root.TrimEnd('\\') + "\\",
            StringComparison.OrdinalIgnoreCase);
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

    private static void ValidateSingleName(string name)
    {
        if (!IsSingleName(name))
        {
            throw new BackupStorageException(BackupFailureKind.InvalidPath);
        }
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

    private sealed class OpenedNode(SafeFileHandle handle, bool isDirectory) : IDisposable
    {
        public SafeFileHandle Handle { get; } = handle;

        public bool IsDirectory { get; } = isDirectory;

        public void Dispose() => Handle.Dispose();
    }

    private sealed class BackupStorageException(BackupFailureKind kind) : Exception
    {
        public BackupFailureKind Kind { get; } = kind;
    }

    private sealed record OwnerMarker(int FormatVersion, string ExecutionId);

    private sealed record ManifestEntry(string Name, string ExpectedKind, string DestinationState);

    private sealed record CompletedManifest(
        int SchemaVersion,
        IReadOnlyList<ManifestEntry> Entries,
        BackupVerificationSummary Verification);

    private sealed record TreeFingerprint(
        int FileCount,
        int DirectoryCount,
        long TotalBytes,
        string Sha256);
}
