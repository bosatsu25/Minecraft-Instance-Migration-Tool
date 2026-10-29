using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Infrastructure.FileSystem;

namespace MinecraftInstanceMigration.Infrastructure.Execution;

// Compares the live Replace destination with the same entry inside the validated backup. Both sides
// use the fingerprint encoding that Backup, verification, and rollback already share, so excluded
// files are ignored consistently and a match proves the backup still covers the destination.
public sealed class WindowsExecutionPreStateProbe : IExecutionPreStateProbe
{
    public Task<ExecutionPreStateResult> CompareWithBackupAsync(
        string destinationRoot,
        string backupRoot,
        ExecutionJournalEntry step,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);

        return Task.Run(
            () => Compare(destinationRoot, backupRoot, step, cancellationToken),
            CancellationToken.None);
    }

    private static ExecutionPreStateResult Compare(
        string destinationRoot,
        string backupRoot,
        ExecutionJournalEntry step,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Pre-state comparison requires Windows.");
        }

        if (step.Operation != ExecutionOperationKind.Replace ||
            !WindowsPathRules.TryNormalizeLocalDirectory(destinationRoot, out string normalizedDestination) ||
            !WindowsPathRules.TryNormalizeLocalDirectory(backupRoot, out string normalizedBackup) ||
            WindowsPathRules.RootsOverlap(normalizedDestination, normalizedBackup))
        {
            return Failed();
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using WindowsExecutionTree.HeldDirectory destination =
                WindowsExecutionTree.OpenDirectoryChain(normalizedDestination, writableFinal: false);
            using WindowsExecutionTree.HeldDirectory backup =
                WindowsExecutionTree.OpenDirectoryChain(normalizedBackup, writableFinal: false);

            if (WindowsExecutionTree.PhysicalRootsOverlap(destination, backup))
            {
                return Failed();
            }

            WindowsExecutionTree.TreeFingerprint current = WindowsExecutionTree.FingerprintNode(
                destination.Root,
                step.Name,
                step.ExpectedKind,
                cancellationToken);
            WindowsExecutionTree.TreeFingerprint preserved = WindowsExecutionTree.FingerprintNode(
                backup.Root,
                step.Name,
                step.ExpectedKind,
                cancellationToken);

            return current.Equals(preserved)
                ? new ExecutionPreStateResult(ExecutionPreStateStatus.Matches, current.ToDomain())
                : new ExecutionPreStateResult(ExecutionPreStateStatus.Changed);
        }
        catch (OperationCanceledException)
        {
            return new ExecutionPreStateResult(ExecutionPreStateStatus.Cancelled);
        }
        catch (Exception error) when (FileSystemErrors.TryClassify(error, out FileSystemFailureKind kind))
        {
            // A missing entry or a kind change on either side means the pair no longer matches.
            return kind is FileSystemFailureKind.Missing or FileSystemFailureKind.Changed
                ? new ExecutionPreStateResult(ExecutionPreStateStatus.Changed)
                : Failed();
        }
    }

    private static ExecutionPreStateResult Failed() =>
        new(ExecutionPreStateStatus.Failed);
}
