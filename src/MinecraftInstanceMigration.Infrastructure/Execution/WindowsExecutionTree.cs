using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Rules;
using MinecraftInstanceMigration.Infrastructure.FileSystem;

namespace MinecraftInstanceMigration.Infrastructure.Execution;

// Handle-relative, no-follow tree operations shared by backup, execution, verification, rollback
// and capacity measurement. There is exactly one copy and one fingerprint algorithm.
internal static class WindowsExecutionTree
{
    internal const uint ReadOnlyDirectoryAccess = 0;

    // Final-component rights needed to create, replace and delete children in a migration root.
    internal const uint MutableDirectoryAccess =
        NtFileApi.FileAddFile |
        NtFileApi.FileAddSubdirectory |
        NtFileApi.FileDeleteChild |
        NtFileApi.FileWriteAttributes;

    private const int BufferSize = 128 * 1024;

    private const uint TraverseDirectoryAccess =
        NtFileApi.FileListDirectory |
        NtFileApi.FileTraverse |
        NtFileApi.FileReadAttributes |
        NtFileApi.Synchronize;

    internal static bool PhysicalRootsOverlap(
        HeldDirectory first,
        HeldDirectory second) =>
        IsPhysicalEqualOrDescendant(first.Root, second.Root) ||
        IsPhysicalEqualOrDescendant(second.Root, first.Root);

    // Compares canonical volume paths of held handles so SUBST or other aliases cannot hide overlap.
    internal static bool IsPhysicalEqualOrDescendant(
        SafeFileHandle candidate,
        SafeFileHandle root) =>
        WindowsPathRules.IsEqualOrDescendant(
            NtFileApi.GetCanonicalVolumePath(candidate),
            NtFileApi.GetCanonicalVolumePath(root));

    internal static HeldDirectory OpenDirectoryChain(
        string path,
        bool writableFinal) =>
        OpenDirectoryChain(path, writableFinal ? MutableDirectoryAccess : ReadOnlyDirectoryAccess);

    // Opens the drive root, then each component relative to the previous held handle. Every
    // ancestor stays open (denying rename/delete sharing) until the returned chain is disposed.
    internal static HeldDirectory OpenDirectoryChain(
        string path,
        uint finalAccess)
    {
        (SafeFileHandle drive, int error) = NtFileApi.OpenDriveRoot(path[..3], TraverseDirectoryAccess);
        if (drive.IsInvalid)
        {
            drive.Dispose();
            throw new FileSystemException(FileSystemErrors.ForWin32Error(error));
        }

        var handles = new List<SafeFileHandle> { drive };
        try
        {
            string[] components = path[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < components.Length; index++)
            {
                bool isFinal = index == components.Length - 1;
                (SafeFileHandle next, int status) = NtFileApi.OpenRelative(
                    handles[^1],
                    components[index],
                    TraverseDirectoryAccess | (isFinal ? finalAccess : 0),
                    NtFileApi.ShareRead,
                    NtFileApi.FileOpen,
                    NtFileApi.NoFollowOpenOptions);

                if (status != 0)
                {
                    next.Dispose();
                    throw FileSystemErrors.ForOpen(status);
                }

                handles.Add(next);
                FileAttributes attributes = NtFileApi.ReadAttributes(next);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new FileSystemException(FileSystemFailureKind.ReparsePoint);
                }

                if (!attributes.HasFlag(FileAttributes.Directory))
                {
                    throw new FileSystemException(FileSystemFailureKind.InvalidPath);
                }
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

        uint shareAccess = NtFileApi.ShareRead | (forDelete ? NtFileApi.ShareDelete : 0);
        (SafeFileHandle metadata, int metadataStatus) = NtFileApi.OpenRelative(
            parent,
            name,
            NtFileApi.FileReadAttributes | NtFileApi.Synchronize,
            shareAccess,
            NtFileApi.FileOpen,
            NtFileApi.NoFollowOpenOptions);

        if (metadataStatus != 0)
        {
            metadata.Dispose();
            throw FileSystemErrors.ForOpen(metadataStatus);
        }

        bool directory;
        using (metadata)
        {
            FileAttributes attributes = NtFileApi.ReadAttributes(metadata);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new FileSystemException(FileSystemFailureKind.ReparsePoint);
            }

            directory = attributes.HasFlag(FileAttributes.Directory);
        }

        uint desiredAccess = NtFileApi.FileReadAttributes | NtFileApi.Synchronize |
            (directory
                ? NtFileApi.FileListDirectory | NtFileApi.FileTraverse
                : NtFileApi.FileReadData);
        if (forDelete)
        {
            desiredAccess |= NtFileApi.DeleteAccess | (directory ? NtFileApi.FileDeleteChild : 0);
        }

        (SafeFileHandle data, int status) = NtFileApi.OpenRelative(
            parent,
            name,
            desiredAccess,
            NtFileApi.ShareRead,
            NtFileApi.FileOpen,
            NtFileApi.NoFollowOpenOptions);

        if (status != 0)
        {
            data.Dispose();
            throw FileSystemErrors.ForOpen(status);
        }

        try
        {
            // The object may have been swapped between the two opens; confirm it on the held handle.
            FileAttributes confirmed = NtFileApi.ReadAttributes(data);
            if (confirmed.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new FileSystemException(FileSystemFailureKind.ReparsePoint);
            }

            if (confirmed.HasFlag(FileAttributes.Directory) != directory)
            {
                throw new FileSystemException(FileSystemFailureKind.Changed);
            }

            return new OpenedNode(data, directory);
        }
        catch
        {
            data.Dispose();
            throw;
        }
    }

    internal static void EnsureExpectedKind(
        OpenedNode node,
        ExpectedEntryKind expectedKind)
    {
        if (node.IsDirectory != (expectedKind == ExpectedEntryKind.Directory))
        {
            throw new FileSystemException(FileSystemFailureKind.Changed);
        }
    }

    internal static void CopyNode(
        OpenedNode source,
        SafeFileHandle destinationParent,
        string name,
        CancellationToken cancellationToken,
        bool allowExistingDirectories = false)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!source.IsDirectory && KnownMigrationContentRules.IsExcludedFileName(name))
        {
            return;
        }

        if (source.IsDirectory)
        {
            using SafeFileHandle destination = allowExistingDirectories
                ? CreateOrOpenDirectory(destinationParent, name)
                : CreateDirectory(destinationParent, name);

            foreach (string childName in NtFileApi.EnumerateNames(source.Handle))
            {
                cancellationToken.ThrowIfCancellationRequested();
                using OpenedNode child = OpenExistingNode(source.Handle, childName);
                CopyNode(child, destination, childName, cancellationToken, allowExistingDirectories);
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
            foreach (string childName in NtFileApi.EnumerateNames(node.Handle))
            {
                cancellationToken.ThrowIfCancellationRequested();
                DeleteNode(node.Handle, childName, cancellationToken);
            }
        }

        NtFileApi.DeleteByHandle(node.Handle);
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
                foreach (string childName in NtFileApi.EnumerateNames(node.Handle))
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
        CancellationToken cancellationToken) =>
        FingerprintHeldBackupPlan([tree], cancellationToken);

    internal static TreeFingerprint FingerprintHeldBackupPlan(
        IReadOnlyList<HeldNodeTree> trees,
        CancellationToken cancellationToken)
    {
        using var builder = new FingerprintBuilder();
        foreach (HeldNodeTree tree in trees)
        {
            AppendHeldFingerprint(tree, tree.Name, builder, cancellationToken);
        }

        return builder.Build();
    }

    internal static void DeleteHeldTree(
        HeldNodeTree tree,
        Action deleted)
    {
        DeleteHeldTreeCore(tree, deleted);
    }

    internal static void CopyHeldTree(
        HeldNodeTree source,
        SafeFileHandle destinationParent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Node.IsDirectory)
        {
            using SafeFileHandle destination = CreateOrOpenDirectory(destinationParent, source.Name);
            foreach (HeldNodeTree child in source.Children)
            {
                if (!IsExcludedFile(child))
                {
                    CopyHeldTree(child, destination, cancellationToken);
                }
            }

            return;
        }

        using SafeFileHandle destinationFile = CreateFile(destinationParent, source.Name);
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

    // Fingerprints planned backup entries in plan order. Kinds are not enforced: a Replace may back
    // up a destination whose kind differs from the catalog, and the digest itself records the kind.
    internal static TreeFingerprint FingerprintBackupEntries(
        SafeFileHandle root,
        BackupPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        using var builder = new FingerprintBuilder();
        foreach (BackupPlanEntry entry in plan.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using OpenedNode node = OpenExistingNode(root, entry.Name);
            AppendFingerprint(node, entry.Name, builder, cancellationToken);
        }

        return builder.Build();
    }

    internal static TreeFingerprint FingerprintOpenedNode(
        OpenedNode node,
        string relativeName,
        CancellationToken cancellationToken)
    {
        using var builder = new FingerprintBuilder();
        AppendFingerprint(node, relativeName, builder, cancellationToken);
        return builder.Build();
    }

    // Replace preserves excluded destination files and the directories that contain them, so a
    // verified destination may legitimately hold extra content made only of excluded files.
    internal static bool MigrationPayloadMatches(
        OpenedNode source,
        OpenedNode destination,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source.IsDirectory != destination.IsDirectory)
        {
            return false;
        }

        if (!source.IsDirectory)
        {
            return FileContentMatches(source.Handle, destination.Handle, cancellationToken);
        }

        // Names arrive sorted case-insensitively; keeping the first match preserves linear-search
        // semantics while avoiding a quadratic scan of large directories such as world saves.
        var destinationNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in NtFileApi.EnumerateNames(destination.Handle))
        {
            destinationNames.TryAdd(name, name);
        }

        var remainingDestinationNames = new HashSet<string>(destinationNames.Values, StringComparer.OrdinalIgnoreCase);

        foreach (string sourceName in NtFileApi.EnumerateNames(source.Handle))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using OpenedNode sourceChild = OpenExistingNode(source.Handle, sourceName);
            if (!sourceChild.IsDirectory && KnownMigrationContentRules.IsExcludedFileName(sourceName))
            {
                continue;
            }

            if (!destinationNames.TryGetValue(sourceName, out string? destinationName))
            {
                return false;
            }

            using OpenedNode destinationChild = OpenExistingNode(destination.Handle, destinationName);
            if (!MigrationPayloadMatches(sourceChild, destinationChild, cancellationToken))
            {
                return false;
            }

            remainingDestinationNames.Remove(destinationName);
        }

        foreach (string destinationName in remainingDestinationNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using OpenedNode destinationChild = OpenExistingNode(destination.Handle, destinationName);
            if (!ContainsOnlyExcludedContent(destinationChild, destinationName, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    internal static long MeasureLogicalBytes(
        OpenedNode node,
        string name,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!node.IsDirectory)
        {
            return KnownMigrationContentRules.IsExcludedFileName(name)
                ? 0
                : RandomAccess.GetLength(node.Handle);
        }

        long totalBytes = 0;
        foreach (string childName in NtFileApi.EnumerateNames(node.Handle))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using OpenedNode child = OpenExistingNode(node.Handle, childName);
            totalBytes = checked(totalBytes + MeasureLogicalBytes(child, childName, cancellationToken));
        }

        return totalBytes;
    }

    internal static SafeFileHandle CreateDirectory(
        SafeFileHandle parent,
        string name)
    {
        ValidateSingleName(name);

        (SafeFileHandle handle, int status) = NtFileApi.OpenRelative(
            parent,
            name,
            NtFileApi.FileListDirectory |
            NtFileApi.FileAddFile |
            NtFileApi.FileAddSubdirectory |
            NtFileApi.FileTraverse |
            NtFileApi.FileReadAttributes |
            NtFileApi.FileWriteAttributes |
            NtFileApi.Synchronize,
            NtFileApi.ShareRead,
            NtFileApi.FileCreate,
            NtFileApi.FileDirectoryFile | NtFileApi.FileSynchronousIoNonAlert);

        if (status != 0)
        {
            handle.Dispose();
            throw FileSystemErrors.ForCreate(status);
        }

        return handle;
    }

    // Create-only: an existing name, including a pre-planted hard link, is a collision, never a target.
    internal static SafeFileHandle CreateFile(
        SafeFileHandle parent,
        string name)
    {
        ValidateSingleName(name);

        (SafeFileHandle handle, int status) = NtFileApi.OpenRelative(
            parent,
            name,
            NtFileApi.FileReadData |
            NtFileApi.FileWriteData |
            NtFileApi.FileReadAttributes |
            NtFileApi.FileWriteAttributes |
            NtFileApi.Synchronize,
            NtFileApi.ShareRead,
            NtFileApi.FileCreate,
            NtFileApi.FileNonDirectoryFile | NtFileApi.FileSynchronousIoNonAlert);

        if (status != 0)
        {
            handle.Dispose();
            throw FileSystemErrors.ForCreate(status);
        }

        return handle;
    }

    private static bool DeleteHeldTreeCore(
        HeldNodeTree tree,
        Action deleted)
    {
        if (IsExcludedFile(tree))
        {
            return false;
        }

        bool canDelete = true;
        foreach (HeldNodeTree child in tree.Children)
        {
            canDelete &= DeleteHeldTreeCore(child, deleted);
            child.Dispose();
        }

        if (!canDelete)
        {
            return false;
        }

        NtFileApi.DeleteByHandle(tree.Node.Handle);
        deleted();
        return true;
    }

    private static void AppendHeldFingerprint(
        HeldNodeTree tree,
        string relativePath,
        FingerprintBuilder builder,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!tree.Node.IsDirectory)
        {
            builder.AppendFile(relativePath, tree.Node.Handle, cancellationToken);
            return;
        }

        builder.AppendDirectory(relativePath);
        foreach (HeldNodeTree child in tree.Children)
        {
            if (!IsExcludedFile(child))
            {
                AppendHeldFingerprint(child, relativePath + "/" + child.Name, builder, cancellationToken);
            }
        }
    }

    // The child is opened before the exclusion check so an excluded name that is a reparse point
    // still fails closed instead of being skipped.
    private static void AppendFingerprint(
        OpenedNode node,
        string relativePath,
        FingerprintBuilder builder,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!node.IsDirectory)
        {
            builder.AppendFile(relativePath, node.Handle, cancellationToken);
            return;
        }

        builder.AppendDirectory(relativePath);
        foreach (string childName in NtFileApi.EnumerateNames(node.Handle))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using OpenedNode child = OpenExistingNode(node.Handle, childName);
            if (!child.IsDirectory && KnownMigrationContentRules.IsExcludedFileName(childName))
            {
                continue;
            }

            AppendFingerprint(child, relativePath + "/" + childName, builder, cancellationToken);
        }
    }

    internal static bool ContainsOnlyExcludedContent(
        OpenedNode node,
        string name,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!node.IsDirectory)
        {
            return KnownMigrationContentRules.IsExcludedFileName(name);
        }

        bool foundExcludedFile = false;
        foreach (string childName in NtFileApi.EnumerateNames(node.Handle))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using OpenedNode child = OpenExistingNode(node.Handle, childName);
            if (!ContainsOnlyExcludedContent(child, childName, cancellationToken))
            {
                return false;
            }

            foundExcludedFile = true;
        }

        return foundExcludedFile;
    }

    private static bool FileContentMatches(
        SafeFileHandle source,
        SafeFileHandle destination,
        CancellationToken cancellationToken)
    {
        long sourceLength = RandomAccess.GetLength(source);
        long destinationLength = RandomAccess.GetLength(destination);
        if (sourceLength != destinationLength)
        {
            return false;
        }

        byte[] sourceBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        byte[] destinationBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            long offset = 0;
            while (offset < sourceLength)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int sourceRead = RandomAccess.Read(source, sourceBuffer.AsSpan(0, BufferSize), offset);
                int destinationRead = RandomAccess.Read(destination, destinationBuffer.AsSpan(0, BufferSize), offset);
                if (sourceRead == 0 ||
                    sourceRead != destinationRead ||
                    !sourceBuffer.AsSpan(0, sourceRead).SequenceEqual(destinationBuffer.AsSpan(0, destinationRead)))
                {
                    return false;
                }

                offset += sourceRead;
            }

            return RandomAccess.GetLength(source) == sourceLength &&
                RandomAccess.GetLength(destination) == destinationLength;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sourceBuffer, clearArray: false);
            ArrayPool<byte>.Shared.Return(destinationBuffer, clearArray: false);
        }
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

    internal static SafeFileHandle CreateOrOpenDirectory(
        SafeFileHandle parent,
        string name)
    {
        try
        {
            return CreateDirectory(parent, name);
        }
        catch (FileSystemException error) when (error.Kind == FileSystemFailureKind.Collision)
        {
            (SafeFileHandle handle, int status) = NtFileApi.OpenRelative(
                parent,
                name,
                NtFileApi.FileListDirectory |
                NtFileApi.FileAddFile |
                NtFileApi.FileAddSubdirectory |
                NtFileApi.FileTraverse |
                NtFileApi.FileReadAttributes |
                NtFileApi.FileWriteAttributes |
                NtFileApi.Synchronize,
                NtFileApi.ShareRead,
                NtFileApi.FileOpen,
                NtFileApi.NoFollowOpenOptions);

            if (status != 0)
            {
                handle.Dispose();
                throw FileSystemErrors.ForOpen(status);
            }

            try
            {
                FileAttributes attributes = NtFileApi.ReadAttributes(handle);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new FileSystemException(FileSystemFailureKind.ReparsePoint);
                }

                if (!attributes.HasFlag(FileAttributes.Directory))
                {
                    throw new FileSystemException(FileSystemFailureKind.Collision);
                }

                return handle;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }
    }

    internal static bool IsExcludedFile(HeldNodeTree tree) =>
        !tree.Node.IsDirectory && KnownMigrationContentRules.IsExcludedFileName(tree.Name);

    private static void ValidateSingleName(string name)
    {
        if (!WindowsPathRules.IsSingleName(name))
        {
            throw new FileSystemException(FileSystemFailureKind.InvalidPath);
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

    // Path-free tree digest persisted in journals and backup manifests. The encoding must stay
    // byte-for-byte stable: [type: 1 = directory, 2 = file][int32 LE UTF-8 path length][path], and
    // for files [int64 LE length][default-stream bytes]. Changing it invalidates existing evidence.
    private sealed class FingerprintBuilder : IDisposable
    {
        private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private int files;
        private int directories;
        private long totalBytes;

        internal void AppendDirectory(string relativePath)
        {
            AppendRecordPrefix(1, relativePath);
            directories++;
        }

        internal void AppendFile(string relativePath, SafeFileHandle handle, CancellationToken cancellationToken)
        {
            AppendRecordPrefix(2, relativePath);
            files++;

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
                    int read = RandomAccess.Read(handle, buffer.AsSpan(0, BufferSize), offset);
                    if (read == 0)
                    {
                        throw new FileSystemException(FileSystemFailureKind.Changed);
                    }

                    hash.AppendData(buffer.AsSpan(0, read));
                    offset += read;
                }

                if (RandomAccess.GetLength(handle) != length)
                {
                    throw new FileSystemException(FileSystemFailureKind.Changed);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
            }
        }

        internal TreeFingerprint Build() =>
            new(files, directories, totalBytes, Convert.ToHexString(hash.GetHashAndReset()));

        public void Dispose() => hash.Dispose();

        private void AppendRecordPrefix(byte type, string relativePath)
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
    }
}
