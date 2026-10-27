using Microsoft.Win32.SafeHandles;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Infrastructure.FileSystem;

namespace MinecraftInstanceMigration.Infrastructure.Execution;

public sealed class WindowsExecutionMutationPort : IExecutionMutationPort
{
    public Task<ExecutionMutationResult> ApplyAsync(
        string sourceRoot,
        string destinationRoot,
        ExecutionJournalEntry step,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);

        return Task.Run(
            () => Apply(sourceRoot, destinationRoot, step, cancellationToken),
            CancellationToken.None);
    }

    private static ExecutionMutationResult Apply(
        string sourceRoot,
        string destinationRoot,
        ExecutionJournalEntry step,
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
                EnsureReplaceDestination(destination.Root, step);
                ReplaceFromSource(
                    sourceNode,
                    destination.Root,
                    step.Name,
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
