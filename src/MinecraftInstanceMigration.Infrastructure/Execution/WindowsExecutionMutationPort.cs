using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Infrastructure.Backup;

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

        if (!WindowsExecutionTree.TryNormalizeRoot(sourceRoot, out string normalizedSource) ||
            !WindowsExecutionTree.TryNormalizeRoot(destinationRoot, out string normalizedDestination))
        {
            return Failed(ExecutionMutationFailureKind.InvalidPath);
        }

        if (WindowsExecutionTree.RootsOverlap(normalizedSource, normalizedDestination))
        {
            return Failed(ExecutionMutationFailureKind.OverlappingRoots);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using WindowsExecutionTree.HeldDirectory source =
                WindowsExecutionTree.OpenDirectoryChain(normalizedSource, writableFinal: false);
            using WindowsExecutionTree.HeldDirectory destination =
                WindowsExecutionTree.OpenDirectoryChain(normalizedDestination, writableFinal: true);

            using WindowsExecutionTree.OpenedNode sourceNode = OpenSource(source.Root, step);

            if (step.Operation == ExecutionOperationKind.Copy)
            {
                EnsureDestinationMissing(destination.Root, step.Name);
                CopyFromSource(sourceNode, destination.Root, step.Name, cancellationToken);
            }
            else if (step.Operation == ExecutionOperationKind.Replace)
            {
                EnsureReplaceDestination(destination.Root, step);
                RemoveDestination(destination.Root, step.Name, cancellationToken);
                CopyFromSource(sourceNode, destination.Root, step.Name, cancellationToken);
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
        catch (ExecutionTreeException error)
        {
            return Failed(MapGeneralFailure(error.Kind));
        }
        catch (UnauthorizedAccessException)
        {
            return Failed(ExecutionMutationFailureKind.AccessDenied);
        }
        catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 5)
        {
            return Failed(ExecutionMutationFailureKind.AccessDenied);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Failed(ExecutionMutationFailureKind.IoFailure);
        }
        catch (IOException)
        {
            return Failed(ExecutionMutationFailureKind.IoFailure);
        }
        catch (BackupNativeException error)
        {
            return Failed(MapNativeFailure(error.Status));
        }
    }

    private static WindowsExecutionTree.OpenedNode OpenSource(
        Microsoft.Win32.SafeHandles.SafeFileHandle sourceRoot,
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
        catch (ExecutionTreeException error) when (
            error.Kind is ExecutionTreeFailureKind.Missing or ExecutionTreeFailureKind.Changed)
        {
            throw new ExecutionMutationException(ExecutionMutationFailureKind.SourceChanged);
        }
    }

    private static void EnsureDestinationMissing(
        Microsoft.Win32.SafeHandles.SafeFileHandle destinationRoot,
        string name)
    {
        try
        {
            using WindowsExecutionTree.OpenedNode _ =
                WindowsExecutionTree.OpenExistingNode(destinationRoot, name);

            throw new ExecutionMutationException(ExecutionMutationFailureKind.DestinationChanged);
        }
        catch (ExecutionTreeException error) when (error.Kind == ExecutionTreeFailureKind.Missing)
        {
        }
        catch (ExecutionTreeException error) when (error.Kind == ExecutionTreeFailureKind.Changed)
        {
            throw new ExecutionMutationException(ExecutionMutationFailureKind.DestinationChanged);
        }
    }

    private static void EnsureReplaceDestination(
        Microsoft.Win32.SafeHandles.SafeFileHandle destinationRoot,
        ExecutionJournalEntry step)
    {
        try
        {
            using WindowsExecutionTree.OpenedNode destination =
                WindowsExecutionTree.OpenExistingNode(destinationRoot, step.Name);

            WindowsExecutionTree.EnsureExpectedKind(destination, step.ExpectedKind);
        }
        catch (ExecutionTreeException error) when (
            error.Kind is ExecutionTreeFailureKind.Missing or ExecutionTreeFailureKind.Changed)
        {
            throw new ExecutionMutationException(ExecutionMutationFailureKind.DestinationChanged);
        }
    }

    private static void CopyFromSource(
        WindowsExecutionTree.OpenedNode source,
        Microsoft.Win32.SafeHandles.SafeFileHandle destinationRoot,
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            WindowsExecutionTree.CopyNode(source, destinationRoot, name, cancellationToken);
        }
        catch (ExecutionTreeException error) when (error.Kind == ExecutionTreeFailureKind.Collision)
        {
            throw new ExecutionMutationException(ExecutionMutationFailureKind.DestinationChanged);
        }
        catch (ExecutionTreeException error) when (
            error.Kind is ExecutionTreeFailureKind.Missing or ExecutionTreeFailureKind.Changed)
        {
            throw new ExecutionMutationException(ExecutionMutationFailureKind.SourceChanged);
        }
    }

    private static void RemoveDestination(
        Microsoft.Win32.SafeHandles.SafeFileHandle destinationRoot,
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            WindowsExecutionTree.DeleteNode(destinationRoot, name, cancellationToken);
        }
        catch (ExecutionTreeException error) when (
            error.Kind is ExecutionTreeFailureKind.Missing or
                ExecutionTreeFailureKind.Changed or
                ExecutionTreeFailureKind.Collision)
        {
            throw new ExecutionMutationException(ExecutionMutationFailureKind.DestinationChanged);
        }
    }

    private static ExecutionMutationFailureKind MapGeneralFailure(ExecutionTreeFailureKind kind) =>
        kind switch
        {
            ExecutionTreeFailureKind.InvalidPath => ExecutionMutationFailureKind.InvalidPath,
            ExecutionTreeFailureKind.OverlappingRoots => ExecutionMutationFailureKind.OverlappingRoots,
            ExecutionTreeFailureKind.ReparsePoint => ExecutionMutationFailureKind.ReparsePoint,
            ExecutionTreeFailureKind.AccessDenied => ExecutionMutationFailureKind.AccessDenied,
            ExecutionTreeFailureKind.Missing or ExecutionTreeFailureKind.Changed =>
                ExecutionMutationFailureKind.SourceChanged,
            ExecutionTreeFailureKind.Collision => ExecutionMutationFailureKind.DestinationChanged,
            _ => ExecutionMutationFailureKind.IoFailure,
        };

    private static ExecutionMutationFailureKind MapNativeFailure(int status)
    {
        if (status == BackupNativeMethods.StatusReparsePointEncountered)
        {
            return ExecutionMutationFailureKind.ReparsePoint;
        }

        return BackupNativeMethods.RtlNtStatusToDosError(status) == 5
            ? ExecutionMutationFailureKind.AccessDenied
            : ExecutionMutationFailureKind.IoFailure;
    }

    private static ExecutionMutationResult Failed(ExecutionMutationFailureKind kind) =>
        new(ExecutionMutationStatus.Failed, kind);

    private sealed class ExecutionMutationException(ExecutionMutationFailureKind kind) : Exception
    {
        internal ExecutionMutationFailureKind Kind { get; } = kind;
    }
}
