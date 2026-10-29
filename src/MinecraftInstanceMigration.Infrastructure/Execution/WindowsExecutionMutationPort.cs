using Microsoft.Win32.SafeHandles;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Infrastructure.FileSystem;

namespace MinecraftInstanceMigration.Infrastructure.Execution;

public sealed class WindowsExecutionMutationPort : IExecutionMutationPort
{
    public Task<ExecutionMutationResult> ApplyAsync(
        string sourceRoot,
        string destinationRoot,
        ExecutionJournalEntry step,
        ExecutionContentFingerprint? expectedDestination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);

        return Task.Run(
            () => Apply(sourceRoot, destinationRoot, step, expectedDestination, cancellationToken),
            CancellationToken.None);
    }

    public Task<ExecutionMutationResult> MergeAsync(
        string sourceRoot,
        string destinationRoot,
        string backupRoot,
        ExecutionJournalEntry step,
        ExecutionContentFingerprint expectedDestination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(expectedDestination);

        return Task.Run(
            () => Merge(sourceRoot, destinationRoot, backupRoot, step, expectedDestination, cancellationToken),
            CancellationToken.None);
    }

    private static ExecutionMutationResult Merge(
        string sourceRoot,
        string destinationRoot,
        string backupRoot,
        ExecutionJournalEntry step,
        ExecutionContentFingerprint expectedDestination,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Execution mutation requires Windows.");
        }

        MergeUnit unit = MergeUnitOf(step.Name);
        if (step.Operation != ExecutionOperationKind.Merge ||
            step.ExpectedKind != ExpectedEntryKind.Directory ||
            unit == MergeUnit.NotOffered)
        {
            return Failed(ExecutionMutationFailureKind.DestinationChanged);
        }

        if (!WindowsPathRules.TryNormalizeLocalDirectory(sourceRoot, out string normalizedSource) ||
            !WindowsPathRules.TryNormalizeLocalDirectory(destinationRoot, out string normalizedDestination) ||
            !WindowsPathRules.TryNormalizeLocalDirectory(backupRoot, out string normalizedBackup))
        {
            return Failed(ExecutionMutationFailureKind.InvalidPath);
        }

        if (WindowsPathRules.RootsOverlap(normalizedSource, normalizedDestination) ||
            WindowsPathRules.RootsOverlap(normalizedBackup, normalizedDestination) ||
            WindowsPathRules.RootsOverlap(normalizedBackup, normalizedSource))
        {
            return Failed(ExecutionMutationFailureKind.OverlappingRoots);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using WindowsExecutionTree.HeldDirectory source = OpenSourceRoot(normalizedSource);
            using WindowsExecutionTree.HeldDirectory destination = OpenDestinationRoot(normalizedDestination);
            using WindowsExecutionTree.HeldDirectory backup = OpenBackupRoot(normalizedBackup);

            if (WindowsExecutionTree.PhysicalRootsOverlap(source, destination) ||
                WindowsExecutionTree.PhysicalRootsOverlap(backup, destination) ||
                WindowsExecutionTree.PhysicalRootsOverlap(backup, source))
            {
                return Failed(ExecutionMutationFailureKind.OverlappingRoots);
            }

            using WindowsExecutionTree.OpenedNode sourceNode = OpenSource(source.Root, step);
            EnsureReplaceDestination(destination.Root, step);

            // The held backup is the kept content of the rebuild; it must be exactly the proven pre-state.
            using WindowsExecutionTree.HeldNodeTree preserved = OpenBackupTree(backup.Root, step.Name);
            if (WindowsExecutionTree.FingerprintHeldTree(preserved, cancellationToken).ToDomain() !=
                expectedDestination)
            {
                throw new ExecutionMutationException(ExecutionMutationFailureKind.DestinationChanged);
            }

            // A kind conflict found now came from the source; refuse it before anything is deleted.
            using (WindowsExecutionTree.OpenedNode preservedNode =
                WindowsExecutionTree.OpenExistingNode(backup.Root, step.Name))
            {
                if (!WindowsMergeTree.Inspect(sourceNode, preservedNode, step.Name, unit, cancellationToken).IsClean)
                {
                    throw new ExecutionMutationException(ExecutionMutationFailureKind.SourceChanged);
                }
            }

            using (WindowsExecutionTree.HeldNodeTree current =
                WindowsExecutionTree.OpenHeldTree(destination.Root, step.Name, forDelete: true))
            {
                if (WindowsExecutionTree.FingerprintHeldTree(current, cancellationToken).ToDomain() !=
                    expectedDestination)
                {
                    throw new ExecutionMutationException(ExecutionMutationFailureKind.DestinationChanged);
                }

                WindowsExecutionTree.DeleteHeldTree(current, () => { });
            }

            WindowsMergeTree.Rebuild(sourceNode, preserved, destination.Root, step.Name, unit, cancellationToken);
            return new ExecutionMutationResult(ExecutionMutationStatus.Applied);
        }
        catch (OperationCanceledException)
        {
            return new ExecutionMutationResult(ExecutionMutationStatus.Cancelled);
        }
        catch (ExecutionMutationException error)
        {
            return Failed(error.Kind);
        }
        catch (Exception error) when (FileSystemErrors.TryClassify(error, out FileSystemFailureKind kind))
        {
            return Failed(MapGeneralFailure(kind));
        }
    }

    private static MergeUnit MergeUnitOf(string name) =>
        KnownEntryCatalog.All
            .SingleOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal))
            ?.MergeUnit ?? MergeUnit.NotOffered;

    private static WindowsExecutionTree.HeldDirectory OpenBackupRoot(string path)
    {
        try
        {
            return WindowsExecutionTree.OpenDirectoryChain(path, writableFinal: false);
        }
        catch (FileSystemException error) when (
            error.Kind is FileSystemFailureKind.Missing or FileSystemFailureKind.Changed)
        {
            throw new ExecutionMutationException(ExecutionMutationFailureKind.DestinationChanged);
        }
    }

    private static WindowsExecutionTree.HeldNodeTree OpenBackupTree(SafeFileHandle backupRoot, string name)
    {
        try
        {
            return WindowsExecutionTree.OpenHeldTree(backupRoot, name, forDelete: false);
        }
        catch (FileSystemException error) when (
            error.Kind is FileSystemFailureKind.Missing or FileSystemFailureKind.Changed)
        {
            throw new ExecutionMutationException(ExecutionMutationFailureKind.DestinationChanged);
        }
    }

    private static ExecutionMutationResult Apply(
        string sourceRoot,
        string destinationRoot,
        ExecutionJournalEntry step,
        ExecutionContentFingerprint? expectedDestination,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Execution mutation requires Windows.");
        }

        if (!WindowsPathRules.TryNormalizeLocalDirectory(sourceRoot, out string normalizedSource) ||
            !WindowsPathRules.TryNormalizeLocalDirectory(destinationRoot, out string normalizedDestination))
        {
            return Failed(ExecutionMutationFailureKind.InvalidPath);
        }

        if (WindowsPathRules.RootsOverlap(normalizedSource, normalizedDestination))
        {
            return Failed(ExecutionMutationFailureKind.OverlappingRoots);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using WindowsExecutionTree.HeldDirectory source =
                OpenSourceRoot(normalizedSource);
            using WindowsExecutionTree.HeldDirectory destination =
                OpenDestinationRoot(normalizedDestination);

            if (WindowsExecutionTree.PhysicalRootsOverlap(source, destination))
            {
                return Failed(ExecutionMutationFailureKind.OverlappingRoots);
            }

            using WindowsExecutionTree.OpenedNode sourceNode =
                OpenSource(source.Root, step);

            if (step.Operation == ExecutionOperationKind.Copy)
            {
                EnsureDestinationMissing(destination.Root, step.Name);
                CopyFromSource(sourceNode, destination.Root, step.Name, cancellationToken);
            }
            else if (step.Operation == ExecutionOperationKind.Replace)
            {
                if (expectedDestination is null)
                {
                    // Without proof that the backup covers the destination, nothing may be deleted.
                    return Failed(ExecutionMutationFailureKind.DestinationChanged);
                }

                EnsureReplaceDestination(destination.Root, step);
                ReplaceFromSource(
                    sourceNode,
                    destination.Root,
                    step.Name,
                    expectedDestination,
                    cancellationToken);
            }
            else
            {
                return Failed(ExecutionMutationFailureKind.SourceChanged);
            }

            return new ExecutionMutationResult(ExecutionMutationStatus.Applied);
        }
        catch (OperationCanceledException)
        {
            return new ExecutionMutationResult(ExecutionMutationStatus.Cancelled);
        }
        catch (ExecutionMutationException error)
        {
            return Failed(error.Kind);
        }
        catch (Exception error) when (FileSystemErrors.TryClassify(error, out FileSystemFailureKind kind))
        {
            return Failed(MapGeneralFailure(kind));
        }
    }

    private static WindowsExecutionTree.HeldDirectory OpenSourceRoot(string path)
    {
        try
        {
            return WindowsExecutionTree.OpenDirectoryChain(path, writableFinal: false);
        }
        catch (FileSystemException error) when (
            error.Kind is FileSystemFailureKind.Missing or FileSystemFailureKind.Changed)
        {
            throw new ExecutionMutationException(ExecutionMutationFailureKind.SourceChanged);
        }
    }

    private static WindowsExecutionTree.HeldDirectory OpenDestinationRoot(string path)
    {
        try
        {
            return WindowsExecutionTree.OpenDirectoryChain(path, writableFinal: true);
        }
        catch (FileSystemException error) when (
            error.Kind is FileSystemFailureKind.Missing or FileSystemFailureKind.Changed)
        {
            throw new ExecutionMutationException(ExecutionMutationFailureKind.DestinationChanged);
        }
    }

    private static WindowsExecutionTree.OpenedNode OpenSource(
        SafeFileHandle sourceRoot,
        ExecutionJournalEntry step)
    {
        try
        {
            WindowsExecutionTree.OpenedNode source =
                WindowsExecutionTree.OpenExistingNode(sourceRoot, step.Name);

            try
            {
                WindowsExecutionTree.EnsureExpectedKind(source, step.ExpectedKind);
                return source;
            }
            catch
            {
                source.Dispose();
                throw;
            }
        }
        catch (FileSystemException error) when (
            error.Kind is FileSystemFailureKind.Missing or FileSystemFailureKind.Changed)
        {
            throw new ExecutionMutationException(ExecutionMutationFailureKind.SourceChanged);
        }
    }

    private static void EnsureDestinationMissing(
        SafeFileHandle destinationRoot,
        string name)
    {
        try
        {
            using WindowsExecutionTree.OpenedNode _ =
                WindowsExecutionTree.OpenExistingNode(destinationRoot, name);

            throw new ExecutionMutationException(ExecutionMutationFailureKind.DestinationChanged);
        }
        catch (FileSystemException error) when (error.Kind == FileSystemFailureKind.Missing)
        {
        }
        catch (FileSystemException error) when (error.Kind == FileSystemFailureKind.Changed)
        {
            throw new ExecutionMutationException(ExecutionMutationFailureKind.DestinationChanged);
        }
    }

    private static void EnsureReplaceDestination(
        SafeFileHandle destinationRoot,
        ExecutionJournalEntry step)
    {
        try
        {
            using WindowsExecutionTree.OpenedNode destination =
                WindowsExecutionTree.OpenExistingNode(destinationRoot, step.Name);

            WindowsExecutionTree.EnsureExpectedKind(destination, step.ExpectedKind);
        }
        catch (FileSystemException error) when (
            error.Kind is FileSystemFailureKind.Missing or FileSystemFailureKind.Changed)
        {
            throw new ExecutionMutationException(ExecutionMutationFailureKind.DestinationChanged);
        }
    }

    private static void CopyFromSource(
        WindowsExecutionTree.OpenedNode source,
        SafeFileHandle destinationRoot,
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            WindowsExecutionTree.CopyNode(source, destinationRoot, name, cancellationToken);
        }
        catch (FileSystemException error) when (error.Kind == FileSystemFailureKind.Collision)
        {
            throw new ExecutionMutationException(ExecutionMutationFailureKind.DestinationChanged);
        }
        catch (FileSystemException error) when (
            error.Kind is FileSystemFailureKind.Missing or FileSystemFailureKind.Changed)
        {
            throw new ExecutionMutationException(ExecutionMutationFailureKind.SourceChanged);
        }
    }

    private static void ReplaceFromSource(
        WindowsExecutionTree.OpenedNode source,
        SafeFileHandle destinationRoot,
        string name,
        ExecutionContentFingerprint expectedDestination,
        CancellationToken cancellationToken)
    {
        try
        {
            using (WindowsExecutionTree.HeldNodeTree destination =
                WindowsExecutionTree.OpenHeldTree(
                    destinationRoot,
                    name,
                    forDelete: true))
            {
                // The held handles deny new writers, so this is the content that will be deleted.
                // Anything other than what the backup was proven to cover must not be removed.
                if (WindowsExecutionTree.FingerprintHeldTree(destination, cancellationToken).ToDomain() !=
                    expectedDestination)
                {
                    throw new ExecutionMutationException(
                        ExecutionMutationFailureKind.DestinationChanged);
                }

                WindowsExecutionTree.DeleteHeldTree(destination, () => { });
            }

            WindowsExecutionTree.CopyNode(
                source,
                destinationRoot,
                name,
                cancellationToken,
                allowExistingDirectories: source.IsDirectory);
        }
        catch (FileSystemException error) when (
            error.Kind is FileSystemFailureKind.Missing or
                FileSystemFailureKind.Changed or
                FileSystemFailureKind.Collision)
        {
            throw new ExecutionMutationException(
                ExecutionMutationFailureKind.DestinationChanged);
        }
    }

    private static ExecutionMutationFailureKind MapGeneralFailure(FileSystemFailureKind kind) =>
        kind switch
        {
            FileSystemFailureKind.InvalidPath => ExecutionMutationFailureKind.InvalidPath,
            FileSystemFailureKind.OverlappingRoots => ExecutionMutationFailureKind.OverlappingRoots,
            FileSystemFailureKind.ReparsePoint => ExecutionMutationFailureKind.ReparsePoint,
            FileSystemFailureKind.AccessDenied => ExecutionMutationFailureKind.AccessDenied,
            FileSystemFailureKind.Missing or FileSystemFailureKind.Changed =>
                ExecutionMutationFailureKind.SourceChanged,
            FileSystemFailureKind.Collision => ExecutionMutationFailureKind.DestinationChanged,
            _ => ExecutionMutationFailureKind.IoFailure,
        };


    private static ExecutionMutationResult Failed(ExecutionMutationFailureKind kind) =>
        new(ExecutionMutationStatus.Failed, kind);

    private sealed class ExecutionMutationException(ExecutionMutationFailureKind kind) : Exception
    {
        internal ExecutionMutationFailureKind Kind { get; } = kind;
    }
}
