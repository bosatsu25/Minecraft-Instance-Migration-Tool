using Microsoft.Win32.SafeHandles;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Infrastructure.Backup;

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

        if (!WindowsExecutionTree.TryNormalizeRoot(sourceRoot, out string normalizedSource) ||
            !WindowsExecutionTree.TryNormalizeRoot(destinationRoot, out string normalizedDestination))
        {
            return Failed(ExecutionPostWriteVerificationFailureKind.InvalidPath);
        }

        if (WindowsExecutionTree.RootsOverlap(normalizedSource, normalizedDestination))
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
        catch (ExecutionTreeException error)
        {
            return Failed(MapGeneralFailure(error.Kind));
        }
        catch (UnauthorizedAccessException)
        {
            return Failed(ExecutionPostWriteVerificationFailureKind.AccessDenied);
        }
        catch (System.ComponentModel.Win32Exception error) when (error.NativeErrorCode == 5)
        {
            return Failed(ExecutionPostWriteVerificationFailureKind.AccessDenied);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Failed(ExecutionPostWriteVerificationFailureKind.IoFailure);
        }
        catch (IOException)
        {
            return Failed(ExecutionPostWriteVerificationFailureKind.IoFailure);
        }
        catch (BackupNativeException error)
        {
            return Failed(MapNativeFailure(error.Status));
        }
    }

    private static WindowsExecutionTree.HeldDirectory OpenSourceRoot(string path)
    {
        try
        {
            return WindowsExecutionTree.OpenDirectoryChain(path, writableFinal: false);
        }
        catch (ExecutionTreeException error) when (
            error.Kind is ExecutionTreeFailureKind.Missing or ExecutionTreeFailureKind.Changed)
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
        catch (ExecutionTreeException error) when (
            error.Kind is ExecutionTreeFailureKind.Missing or ExecutionTreeFailureKind.Changed)
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
        catch (ExecutionTreeException error) when (
            error.Kind is ExecutionTreeFailureKind.Missing or ExecutionTreeFailureKind.Changed)
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
        catch (ExecutionTreeException error) when (
            error.Kind is ExecutionTreeFailureKind.Missing or ExecutionTreeFailureKind.Changed)
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
            catch (ExecutionTreeException error) when (
                error.Kind is ExecutionTreeFailureKind.Missing or ExecutionTreeFailureKind.Changed)
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
        catch (ExecutionTreeException error) when (error.Kind == ExecutionTreeFailureKind.Missing)
        {
            throw new ExecutionVerificationException(
                ExecutionPostWriteVerificationFailureKind.DestinationMissing);
        }
        catch (ExecutionTreeException error) when (error.Kind == ExecutionTreeFailureKind.Changed)
        {
            throw new ExecutionVerificationException(
                ExecutionPostWriteVerificationFailureKind.VerificationMismatch);
        }
    }

    private static ExecutionPostWriteVerificationFailureKind MapGeneralFailure(
        ExecutionTreeFailureKind kind) =>
        kind switch
        {
            ExecutionTreeFailureKind.InvalidPath =>
                ExecutionPostWriteVerificationFailureKind.InvalidPath,
            ExecutionTreeFailureKind.OverlappingRoots =>
                ExecutionPostWriteVerificationFailureKind.OverlappingRoots,
            ExecutionTreeFailureKind.ReparsePoint =>
                ExecutionPostWriteVerificationFailureKind.ReparsePoint,
            ExecutionTreeFailureKind.AccessDenied =>
                ExecutionPostWriteVerificationFailureKind.AccessDenied,
            ExecutionTreeFailureKind.Missing =>
                ExecutionPostWriteVerificationFailureKind.DestinationMissing,
            ExecutionTreeFailureKind.Changed or ExecutionTreeFailureKind.Collision =>
                ExecutionPostWriteVerificationFailureKind.VerificationMismatch,
            _ => ExecutionPostWriteVerificationFailureKind.IoFailure,
        };

    private static ExecutionPostWriteVerificationFailureKind MapNativeFailure(int status)
    {
        if (status == BackupNativeMethods.StatusReparsePointEncountered)
        {
            return ExecutionPostWriteVerificationFailureKind.ReparsePoint;
        }

        return BackupNativeMethods.RtlNtStatusToDosError(status) == 5
            ? ExecutionPostWriteVerificationFailureKind.AccessDenied
            : ExecutionPostWriteVerificationFailureKind.IoFailure;
    }

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
