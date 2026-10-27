using Microsoft.Win32.SafeHandles;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Infrastructure.FileSystem;

namespace MinecraftInstanceMigration.Infrastructure.Execution;

public sealed class WindowsExecutionPostWriteVerifier : IExecutionPostWriteVerifier
{
    public Task<ExecutionPostWriteVerificationResult> VerifyAsync(
        string sourceRoot,
        string destinationRoot,
        ExecutionJournalEntry step,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);

        return Task.Run(
            () => Verify(sourceRoot, destinationRoot, step, cancellationToken),
            CancellationToken.None);
    }

    private static ExecutionPostWriteVerificationResult Verify(
        string sourceRoot,
        string destinationRoot,
        ExecutionJournalEntry step,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Post-write verification requires Windows.");
        }

        if (!WindowsPathRules.TryNormalizeLocalDirectory(sourceRoot, out string normalizedSource) ||
            !WindowsPathRules.TryNormalizeLocalDirectory(destinationRoot, out string normalizedDestination))
        {
            return Failed(ExecutionPostWriteVerificationFailureKind.InvalidPath);
        }

        if (WindowsPathRules.RootsOverlap(normalizedSource, normalizedDestination))
        {
            return Failed(ExecutionPostWriteVerificationFailureKind.OverlappingRoots);
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
                return Failed(ExecutionPostWriteVerificationFailureKind.OverlappingRoots);
            }

            WindowsExecutionTree.TreeFingerprint sourceBefore =
                FingerprintSource(source.Root, step, cancellationToken);
            WindowsExecutionTree.TreeFingerprint destinationFingerprint =
                FingerprintDestination(destination.Root, step, cancellationToken);
            WindowsExecutionTree.TreeFingerprint sourceAfter =
                FingerprintSource(source.Root, step, cancellationToken);

            if (!sourceBefore.Equals(sourceAfter))
            {
                return Failed(ExecutionPostWriteVerificationFailureKind.SourceChanged);
            }

            if (!sourceAfter.Equals(destinationFingerprint) &&
                !PayloadMatches(
                    source.Root,
                    destination.Root,
                    step,
                    cancellationToken))
            {
                return Failed(ExecutionPostWriteVerificationFailureKind.VerificationMismatch);
            }

            return new ExecutionPostWriteVerificationResult(
                ExecutionPostWriteVerificationStatus.Verified,
                destinationFingerprint.ToDomain());
        }
        catch (OperationCanceledException)
        {
            return new ExecutionPostWriteVerificationResult(
                ExecutionPostWriteVerificationStatus.Cancelled);
        }
        catch (ExecutionVerificationException error)
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
            throw new ExecutionVerificationException(
                ExecutionPostWriteVerificationFailureKind.SourceChanged);
        }
    }

    private static WindowsExecutionTree.HeldDirectory OpenDestinationRoot(string path)
    {
        try
        {
            return WindowsExecutionTree.OpenDirectoryChain(path, writableFinal: false);
        }
        catch (FileSystemException error) when (
            error.Kind is FileSystemFailureKind.Missing or FileSystemFailureKind.Changed)
        {
            throw new ExecutionVerificationException(
                ExecutionPostWriteVerificationFailureKind.DestinationMissing);
        }
    }

    private static WindowsExecutionTree.TreeFingerprint FingerprintSource(
        SafeFileHandle sourceRoot,
        ExecutionJournalEntry step,
        CancellationToken cancellationToken)
    {
        try
        {
            return WindowsExecutionTree.FingerprintNode(
                sourceRoot,
                step.Name,
                step.ExpectedKind,
                cancellationToken);
        }
        catch (FileSystemException error) when (
            error.Kind is FileSystemFailureKind.Missing or FileSystemFailureKind.Changed)
        {
            throw new ExecutionVerificationException(
                ExecutionPostWriteVerificationFailureKind.SourceChanged);
        }
    }

    private static bool PayloadMatches(
        SafeFileHandle sourceRoot,
        SafeFileHandle destinationRoot,
        ExecutionJournalEntry step,
        CancellationToken cancellationToken)
    {
        WindowsExecutionTree.OpenedNode source;
        try
        {
            source = WindowsExecutionTree.OpenExistingNode(sourceRoot, step.Name);
            try
            {
                WindowsExecutionTree.EnsureExpectedKind(source, step.ExpectedKind);
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
            throw new ExecutionVerificationException(
                ExecutionPostWriteVerificationFailureKind.SourceChanged);
        }

        using (source)
        {
            WindowsExecutionTree.OpenedNode destination;
            try
            {
                destination = WindowsExecutionTree.OpenExistingNode(
                    destinationRoot,
                    step.Name);
                try
                {
                    WindowsExecutionTree.EnsureExpectedKind(
                        destination,
                        step.ExpectedKind);
                }
                catch
                {
                    destination.Dispose();
                    throw;
                }
            }
            catch (FileSystemException error) when (
                error.Kind is FileSystemFailureKind.Missing or FileSystemFailureKind.Changed)
            {
                throw new ExecutionVerificationException(
                    ExecutionPostWriteVerificationFailureKind.DestinationMissing);
            }

            using (destination)
            {
                return WindowsExecutionTree.MigrationPayloadMatches(
                    source,
                    destination,
                    cancellationToken);
            }
        }
    }

    private static WindowsExecutionTree.TreeFingerprint FingerprintDestination(
        SafeFileHandle destinationRoot,
        ExecutionJournalEntry step,
        CancellationToken cancellationToken)
    {
        try
        {
            return WindowsExecutionTree.FingerprintNode(
                destinationRoot,
                step.Name,
                step.ExpectedKind,
                cancellationToken);
        }
        catch (FileSystemException error) when (error.Kind == FileSystemFailureKind.Missing)
        {
            throw new ExecutionVerificationException(
                ExecutionPostWriteVerificationFailureKind.DestinationMissing);
        }
        catch (FileSystemException error) when (error.Kind == FileSystemFailureKind.Changed)
        {
            throw new ExecutionVerificationException(
                ExecutionPostWriteVerificationFailureKind.VerificationMismatch);
        }
    }

    private static ExecutionPostWriteVerificationFailureKind MapGeneralFailure(
        FileSystemFailureKind kind) =>
        kind switch
        {
            FileSystemFailureKind.InvalidPath =>
                ExecutionPostWriteVerificationFailureKind.InvalidPath,
            FileSystemFailureKind.OverlappingRoots =>
                ExecutionPostWriteVerificationFailureKind.OverlappingRoots,
            FileSystemFailureKind.ReparsePoint =>
                ExecutionPostWriteVerificationFailureKind.ReparsePoint,
            FileSystemFailureKind.AccessDenied =>
                ExecutionPostWriteVerificationFailureKind.AccessDenied,
            FileSystemFailureKind.Missing =>
                ExecutionPostWriteVerificationFailureKind.DestinationMissing,
            FileSystemFailureKind.Changed or FileSystemFailureKind.Collision =>
                ExecutionPostWriteVerificationFailureKind.VerificationMismatch,
            _ => ExecutionPostWriteVerificationFailureKind.IoFailure,
        };


    private static ExecutionPostWriteVerificationResult Failed(
        ExecutionPostWriteVerificationFailureKind kind) =>
        new(
            ExecutionPostWriteVerificationStatus.Failed,
            FailureKind: kind);

    private sealed class ExecutionVerificationException(
        ExecutionPostWriteVerificationFailureKind kind) : Exception
    {
        internal ExecutionPostWriteVerificationFailureKind Kind { get; } = kind;
    }
}
