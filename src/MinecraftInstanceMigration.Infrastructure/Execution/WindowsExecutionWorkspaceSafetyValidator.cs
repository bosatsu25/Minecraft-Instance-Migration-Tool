using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Infrastructure.FileSystem;

namespace MinecraftInstanceMigration.Infrastructure.Execution;

public sealed class WindowsExecutionWorkspaceSafetyValidator
    : IExecutionWorkspaceSafetyValidator
{
    public Task<ExecutionWorkspaceSafetyResult> ValidateAsync(
        string sourceRoot,
        string destinationRoot,
        string journalParent,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Validate(
                sourceRoot,
                destinationRoot,
                journalParent,
                cancellationToken),
            CancellationToken.None);

    private static ExecutionWorkspaceSafetyResult Validate(
        string sourceRoot,
        string destinationRoot,
        string journalParent,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Execution workspace validation requires Windows.");
        }

        if (!WindowsPathRules.TryNormalizeLocalDirectory(sourceRoot, out string source) ||
            !WindowsPathRules.TryNormalizeLocalDirectory(destinationRoot, out string destination) ||
            !WindowsPathRules.TryNormalizeLocalDirectory(journalParent, out string journal))
        {
            return Invalid(ExecutionWorkspaceSafetyFailureKind.InvalidPath);
        }

        if (WindowsPathRules.RootsOverlap(source, destination))
        {
            return Invalid(ExecutionWorkspaceSafetyFailureKind.OverlappingRoots);
        }

        if (WindowsPathRules.IsEqualOrDescendant(journal, source) ||
            WindowsPathRules.IsEqualOrDescendant(journal, destination))
        {
            return Invalid(
                ExecutionWorkspaceSafetyFailureKind.JournalInsideMigrationRoot);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using WindowsExecutionTree.HeldDirectory sourceRootHandle =
                WindowsExecutionTree.OpenDirectoryChain(
                    source,
                    writableFinal: false);
            using WindowsExecutionTree.HeldDirectory destinationRootHandle =
                WindowsExecutionTree.OpenDirectoryChain(
                    destination,
                    writableFinal: false);
            using WindowsExecutionTree.HeldDirectory journalParentHandle =
                WindowsExecutionTree.OpenDirectoryChain(
                    journal,
                    writableFinal: false);

            cancellationToken.ThrowIfCancellationRequested();

            if (WindowsExecutionTree.PhysicalRootsOverlap(
                    sourceRootHandle,
                    destinationRootHandle))
            {
                return Invalid(
                    ExecutionWorkspaceSafetyFailureKind.OverlappingRoots);
            }

            if (WindowsExecutionTree.IsPhysicalEqualOrDescendant(
                    journalParentHandle.Root,
                    sourceRootHandle.Root) ||
                WindowsExecutionTree.IsPhysicalEqualOrDescendant(
                    journalParentHandle.Root,
                    destinationRootHandle.Root))
            {
                return Invalid(
                    ExecutionWorkspaceSafetyFailureKind.JournalInsideMigrationRoot);
            }

            return new ExecutionWorkspaceSafetyResult(
                ExecutionWorkspaceSafetyStatus.Safe);
        }
        catch (OperationCanceledException)
        {
            return new ExecutionWorkspaceSafetyResult(
                ExecutionWorkspaceSafetyStatus.Cancelled);
        }
        catch (Exception error) when (FileSystemErrors.TryClassify(error, out FileSystemFailureKind kind))
        {
            return Invalid(kind switch
            {
                FileSystemFailureKind.ReparsePoint =>
                    ExecutionWorkspaceSafetyFailureKind.ReparsePoint,
                FileSystemFailureKind.AccessDenied =>
                    ExecutionWorkspaceSafetyFailureKind.AccessDenied,
                FileSystemFailureKind.InvalidPath or
                FileSystemFailureKind.Missing or
                FileSystemFailureKind.Changed =>
                    ExecutionWorkspaceSafetyFailureKind.InvalidPath,
                _ => ExecutionWorkspaceSafetyFailureKind.IoFailure,
            });
        }
    }

    private static ExecutionWorkspaceSafetyResult Invalid(
        ExecutionWorkspaceSafetyFailureKind kind) =>
        new(
            ExecutionWorkspaceSafetyStatus.Invalid,
            kind);
}
