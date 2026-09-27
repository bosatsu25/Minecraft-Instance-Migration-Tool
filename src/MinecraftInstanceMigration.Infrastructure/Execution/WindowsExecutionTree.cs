using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Infrastructure.Backup;

namespace MinecraftInstanceMigration.Infrastructure.Execution;

internal enum ExecutionTreeFailureKind
{
    InvalidPath,
    OverlappingRoots,
    Missing,
    Changed,
    Collision,
    ReparsePoint,
    AccessDenied,
    IoFailure,
}

internal sealed class ExecutionTreeException(
    ExecutionTreeFailureKind kind) : Exception
{
    internal ExecutionTreeFailureKind Kind { get; } = kind;
}

internal static class WindowsExecutionTree
{
    private const int BufferSize = 128 * 1024;

    internal static bool TryNormalizeRoot(
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

    internal static bool RootsOverlap(
        string first,
        string second) =>
        IsEqualOrDescendant(first, second) ||
        IsEqualOrDescendant(second, first);

    internal static bool PhysicalRootsOverlap(
        HeldDirectory first,
        HeldDirectory second) =>
        IsPhysicalEqualOrDescendant(first.Root, second.Root) ||
        IsPhysicalEqualOrDescendant(second.Root, first.Root);

    internal static bool IsPhysicalEqualOrDescendant(
        SafeFileHandle candidate,
        SafeFileHandle root)
    {
        string candidatePath =
            ExecutionNativeMethods.GetCanonicalVolumePath(candidate);
        string rootPath =
            ExecutionNativeMethods.GetCanonicalVolumePath(root);

        return IsEqualOrDescendant(candidatePath, rootPath);
    }

    internal static HeldDirectory OpenDirectoryChain(
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
            throw new ExecutionTreeException(error == 5
                ? ExecutionTreeFailureKind.AccessDenied
                : ExecutionTreeFailureKind.IoFailure);
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
                        BackupNativeMethods.FileAddSubdirectory |
                        ExecutionNativeMethods.FileDeleteChild |
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
                    throw MapNativeFailure(status);
                }

                FileAttributes attributes = BackupNativeMethods.ReadAttributes(next);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    next.Dispose();
                    throw new ExecutionTreeException(ExecutionTreeFailureKind.ReparsePoint);
                }

                if (!attributes.HasFlag(FileAttributes.Directory))
                {
                    next.Dispose();
                    throw new ExecutionTreeException(ExecutionTreeFailureKind.InvalidPath);
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

    internal static OpenedNode OpenExistingNode(
        SafeFileHandle parent,
        string name,
        bool forDelete = false)
    {
        ValidateSingleName(name);

        uint shareAccess = BackupNativeMethods.ShareRead |
            (forDelete ? BackupNativeMethods.ShareDelete : 0);

        (SafeFileHandle metadata, int metadataStatus) = BackupNativeMethods.OpenRelative(
            parent,
            name,
            BackupNativeMethods.FileReadAttributes |
            BackupNativeMethods.Synchronize,
            shareAccess,
            BackupNativeMethods.FileOpen,
            BackupNativeMethods.FileOpenReparsePoint |
            BackupNativeMethods.FileOpenNoRecall |
            BackupNativeMethods.FileSynchronousIoNonAlert);

        if (metadataStatus != 0)
        {
            metadata.Dispose();
            throw MapNativeFailure(metadataStatus);
        }

        bool directory;
        using (metadata)
        {
            FileAttributes attributes = BackupNativeMethods.ReadAttributes(metadata);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new ExecutionTreeException(ExecutionTreeFailureKind.ReparsePoint);
            }

            directory = attributes.HasFlag(FileAttributes.Directory);
        }

        uint desiredAccess =
                BackupNativeMethods.FileReadAttributes |
                BackupNativeMethods.Synchronize;

        if (forDelete)
        {
            desiredAccess |= ExecutionNativeMethods.DeleteAccess;
            desiredAccess |= directory
                ? BackupNativeMethods.FileListDirectory |
                    BackupNativeMethods.FileTraverse |
                    ExecutionNativeMethods.FileDeleteChild
                : BackupNativeMethods.FileReadData;
        }
        else
        {
            desiredAccess |= directory
                ? BackupNativeMethods.FileListDirectory |
                    BackupNativeMethods.FileTraverse
                : BackupNativeMethods.FileReadData;
        }

        (SafeFileHandle data, int status) = BackupNativeMethods.OpenRelative(
                parent,
                name,
                desiredAccess,
                BackupNativeMethods.ShareRead,
                BackupNativeMethods.FileOpen,
                BackupNativeMethods.FileOpenReparsePoint |
                BackupNativeMethods.FileOpenNoRecall |
                BackupNativeMethods.FileSynchronousIoNonAlert);

        if (status != 0)
        {
            data.Dispose();
            throw MapNativeFailure(status);
        }

        FileAttributes confirmed = BackupNativeMethods.ReadAttributes(data);
        if (confirmed.HasFlag(FileAttributes.ReparsePoint))
        {
            data.Dispose();
            throw new ExecutionTreeException(ExecutionTreeFailureKind.ReparsePoint);
        }

        bool confirmedDirectory = confirmed.HasFlag(FileAttributes.Directory);
        if (confirmedDirectory != directory)
        {
            data.Dispose();
            throw new ExecutionTreeException(ExecutionTreeFailureKind.Changed);
        }

        return new OpenedNode(data, directory);
    }

    internal static void EnsureExpectedKind(
        OpenedNode node,
        ExpectedEntryKind expectedKind)
    {
        bool expectedDirectory = expectedKind == ExpectedEntryKind.Directory;
        if (node.IsDirectory != expectedDirectory)
        {
            throw new ExecutionTreeException(ExecutionTreeFailureKind.Changed);
        }
    }

    internal static void CopyNode(
        OpenedNode source,
        SafeFileHandle destinationParent,
        string name,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (source.IsDirectory)
        {
            using SafeFileHandle destination = CreateDirectory(destinationParent, name);

            foreach (string childName in BackupNativeMethods.EnumerateNames(source.Handle))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateSingleName(childName);
                using OpenedNode child = OpenExistingNode(source.Handle, childName);
                CopyNode(child, destination, childName, cancellationToken);
            }

            return;
        }

        using SafeFileHandle destinationFile = CreateFile(destinationParent, name);
        CopyFileBytes(source.Handle, destinationFile, cancellationToken);
    }

    internal static void DeleteNode(
        SafeFileHandle parent,
        string name,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using OpenedNode node = OpenExistingNode(parent, name, forDelete: true);
        DeleteOpenedNode(node, cancellationToken);
    }

    internal static void DeleteOpenedNode(
        OpenedNode node,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (node.IsDirectory)
        {
            foreach (string childName in BackupNativeMethods.EnumerateNames(node.Handle))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateSingleName(childName);
                DeleteNode(node.Handle, childName, cancellationToken);
            }
        }

        ExecutionNativeMethods.DeleteByHandle(node.Handle);
    }

    // Retain every descendant handle from fingerprinting until deletion or restore is complete.
    // ShareRead denies new writers and deleters, including for nested entries.
    internal static HeldNodeTree OpenHeldTree(
        SafeFileHandle parent,
        string name,
        bool forDelete)
    {
        OpenedNode node = OpenExistingNode(parent, name, forDelete);
        var children = new List<HeldNodeTree>();
        try
        {
            if (node.IsDirectory)
            {
                foreach (string childName in BackupNativeMethods.EnumerateNames(node.Handle))
                {
                    children.Add(OpenHeldTree(node.Handle, childName, forDelete));
                }
            }

            return new HeldNodeTree(name, node, children);
        }
        catch
        {
            foreach (HeldNodeTree child in children)
            {
                child.Dispose();
            }

            node.Dispose();
            throw;
        }
    }

    internal static TreeFingerprint FingerprintHeldTree(
        HeldNodeTree tree,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int files = 0;
        int directories = 0;
        long totalBytes = 0;
        AppendHeldFingerprint(tree, tree.Name, hash,
            ref files, ref directories, ref totalBytes, cancellationToken);
        return new TreeFingerprint(files, directories, totalBytes,
            Convert.ToHexString(hash.GetHashAndReset()));
    }

    internal static TreeFingerprint FingerprintHeldBackupPlan(
        IReadOnlyList<HeldNodeTree> trees,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int files = 0;
        int directories = 0;
        long totalBytes = 0;
        foreach (HeldNodeTree tree in trees)
        {
            AppendHeldFingerprint(tree, tree.Name, hash,
                ref files, ref directories, ref totalBytes, cancellationToken);
        }

        return new TreeFingerprint(files, directories, totalBytes,
            Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static void AppendHeldFingerprint(
        HeldNodeTree tree,
        string relativePath,
        IncrementalHash hash,
        ref int files,
        ref int directories,
        ref long totalBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppendRecordPrefix(hash, tree.Node.IsDirectory ? (byte)1 : (byte)2,
            relativePath);
        if (tree.Node.IsDirectory)
        {
            directories++;
            foreach (HeldNodeTree child in tree.Children)
            {
                AppendHeldFingerprint(child, relativePath + "/" + child.Name,
                    hash, ref files, ref directories, ref totalBytes,
                    cancellationToken);
            }

            return;
        }

        files++;
        AppendFileContent(tree.Node.Handle, hash, ref totalBytes,
            cancellationToken);
    }

    internal static void DeleteHeldTree(
        HeldNodeTree tree,
        Action deleted)
    {
        foreach (HeldNodeTree child in tree.Children)
        {
            DeleteHeldTree(child, deleted);
            child.Dispose();
        }

        ExecutionNativeMethods.DeleteByHandle(tree.Node.Handle);
        deleted();
    }

    internal static void CopyHeldTree(
        HeldNodeTree source,
        SafeFileHandle destinationParent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Node.IsDirectory)
        {
            using SafeFileHandle destination =
                CreateDirectory(destinationParent, source.Name);
            foreach (HeldNodeTree child in source.Children)
            {
                CopyHeldTree(child, destination, cancellationToken);
            }

            return;
        }

        using SafeFileHandle destinationFile =
            CreateFile(destinationParent, source.Name);
        CopyFileBytes(source.Node.Handle, destinationFile, cancellationToken);
    }

    internal static TreeFingerprint FingerprintNode(
        SafeFileHandle root,
        string name,
        ExpectedEntryKind expectedKind,
        CancellationToken cancellationToken)
    {
        using OpenedNode node = OpenExistingNode(root, name);
        EnsureExpectedKind(node, expectedKind);
        return FingerprintOpenedNode(node, name, cancellationToken);
    }

    internal static TreeFingerprint FingerprintBackupPlan(
        SafeFileHandle root,
        BackupPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int files = 0;
        int directories = 0;
        long totalBytes = 0;

        foreach (BackupPlanEntry entry in plan.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using OpenedNode node = OpenExistingNode(root, entry.Name);
            EnsureExpectedKind(node, entry.ExpectedKind);
            AppendFingerprint(
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

    internal static TreeFingerprint FingerprintOpenedNode(
        OpenedNode node,
        string relativeName,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int files = 0;
        int directories = 0;
        long totalBytes = 0;

        AppendFingerprint(
            node,
            relativeName,
            hash,
            ref files,
            ref directories,
            ref totalBytes,
            cancellationToken);

        return new TreeFingerprint(
            files,
            directories,
            totalBytes,
            Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static void AppendFingerprint(
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
                AppendFingerprint(
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
        AppendFileContent(node.Handle, hash, ref totalBytes,
            cancellationToken);
    }

    private static void AppendFileContent(
        SafeFileHandle handle,
        IncrementalHash hash,
        ref long totalBytes,
        CancellationToken cancellationToken)
    {
        long length = RandomAccess.GetLength(handle);
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
                int read = RandomAccess.Read(
                    handle,
                    buffer.AsSpan(0, BufferSize),
                    offset);

                if (read == 0)
                {
                    throw new ExecutionTreeException(ExecutionTreeFailureKind.Changed);
                }

                hash.AppendData(buffer.AsSpan(0, read));
                offset += read;
            }

            if (RandomAccess.GetLength(handle) != length)
            {
                throw new ExecutionTreeException(ExecutionTreeFailureKind.Changed);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
        }
    }

    private static void AppendRecordPrefix(
        IncrementalHash hash,
        byte type,
        string relativePath)
    {
        Span<byte> typeBytes = stackalloc byte[1];
        typeBytes[0] = type;
        hash.AppendData(typeBytes);

        byte[] path = Encoding.UTF8.GetBytes(relativePath);
        Span<byte> pathLength = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(pathLength, path.Length);
        hash.AppendData(pathLength);
        hash.AppendData(path);
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
                int read = RandomAccess.Read(
                    source,
                    buffer.AsSpan(0, BufferSize),
                    offset);

                if (read == 0)
                {
                    break;
                }

                RandomAccess.Write(
                    destination,
                    buffer.AsSpan(0, read),
                    offset);
                offset += read;
            }

            RandomAccess.FlushToDisk(destination);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
        }
    }

    private static SafeFileHandle CreateDirectory(
        SafeFileHandle parent,
        string name)
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
            throw MapCreateFailure(status);
        }

        return handle;
    }

    private static SafeFileHandle CreateFile(
        SafeFileHandle parent,
        string name)
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
            throw MapCreateFailure(status);
        }

        return handle;
    }

    private static ExecutionTreeException MapCreateFailure(int status)
    {
        if (status == BackupNativeMethods.StatusReparsePointEncountered)
        {
            return new ExecutionTreeException(ExecutionTreeFailureKind.ReparsePoint);
        }

        uint error = BackupNativeMethods.RtlNtStatusToDosError(status);
        return error is 80 or 183
            ? new ExecutionTreeException(ExecutionTreeFailureKind.Collision)
            : MapNativeFailure(status);
    }

    private static ExecutionTreeException MapNativeFailure(int status)
    {
        if (status == BackupNativeMethods.StatusReparsePointEncountered)
        {
            return new ExecutionTreeException(ExecutionTreeFailureKind.ReparsePoint);
        }

        uint error = BackupNativeMethods.RtlNtStatusToDosError(status);
        return new ExecutionTreeException(error switch
        {
            2 or 3 => ExecutionTreeFailureKind.Missing,
            5 => ExecutionTreeFailureKind.AccessDenied,
            80 or 183 => ExecutionTreeFailureKind.Changed,
            123 or 161 or 206 => ExecutionTreeFailureKind.InvalidPath,
            _ => ExecutionTreeFailureKind.IoFailure,
        });
    }

    private static bool IsEqualOrDescendant(
        string candidate,
        string root)
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
            throw new ExecutionTreeException(ExecutionTreeFailureKind.InvalidPath);
        }
    }

    internal sealed class HeldDirectory(
        List<SafeFileHandle> handles) : IDisposable
    {
        internal SafeFileHandle Root => handles[^1];

        public void Dispose()
        {
            foreach (SafeFileHandle handle in handles)
            {
                handle.Dispose();
            }
        }
    }

    internal sealed class OpenedNode(
        SafeFileHandle handle,
        bool isDirectory) : IDisposable
    {
        internal SafeFileHandle Handle { get; } = handle;

        internal bool IsDirectory { get; } = isDirectory;

        public void Dispose() => Handle.Dispose();
    }

    internal sealed class HeldNodeTree(
        string name,
        OpenedNode node,
        IReadOnlyList<HeldNodeTree> children) : IDisposable
    {
        internal string Name { get; } = name;

        internal OpenedNode Node { get; } = node;

        internal IReadOnlyList<HeldNodeTree> Children { get; } = children;

        public void Dispose()
        {
            foreach (HeldNodeTree child in Children)
            {
                child.Dispose();
            }

            Node.Dispose();
        }
    }

    internal sealed record TreeFingerprint(
        int FileCount,
        int DirectoryCount,
        long TotalBytes,
        string Sha256)
    {
        internal ExecutionContentFingerprint ToDomain() =>
            new(FileCount, DirectoryCount, TotalBytes, Sha256);
    }
}
