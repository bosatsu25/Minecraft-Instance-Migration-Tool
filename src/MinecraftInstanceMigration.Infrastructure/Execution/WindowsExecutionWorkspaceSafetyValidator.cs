using MinecraftInstanceMigration.Application.Execution;

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

        if (!WindowsExecutionTree.TryNormalizeRoot(sourceRoot, out string source) ||
            !WindowsExecutionTree.TryNormalizeRoot(destinationRoot, out string destination) ||
            !WindowsExecutionTree.TryNormalizeRoot(journalParent, out string journal))
        {
            return Invalid(ExecutionWorkspaceSafetyFailureKind.InvalidPath);
        }

        if (WindowsExecutionTree.RootsOverlap(source, destination))
        {
            return Invalid(ExecutionWorkspaceSafetyFailureKind.OverlappingRoots);
        }

        if (IsEqualOrDescendant(journal, source) ||
            IsEqualOrDescendant(journal, destination))
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
        catch (ExecutionTreeException error)
        {
            return Invalid(error.Kind switch
            {
                ExecutionTreeFailureKind.ReparsePoint =>
                    ExecutionWorkspaceSafetyFailureKind.ReparsePoint,
                ExecutionTreeFailureKind.AccessDenied =>
                    ExecutionWorkspaceSafetyFailureKind.AccessDenied,
                ExecutionTreeFailureKind.InvalidPath or
                ExecutionTreeFailureKind.Missing or
                ExecutionTreeFailureKind.Changed =>
                    ExecutionWorkspaceSafetyFailureKind.InvalidPath,
                _ => ExecutionWorkspaceSafetyFailureKind.IoFailure,
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Invalid(
                ExecutionWorkspaceSafetyFailureKind.AccessDenied);
        }
        catch (System.ComponentModel.Win32Exception error)
            when (error.NativeErrorCode == 5)
        {
            return Invalid(
                ExecutionWorkspaceSafetyFailureKind.AccessDenied);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Invalid(
                ExecutionWorkspaceSafetyFailureKind.IoFailure);
        }
        catch (IOException)
        {
            return Invalid(
                ExecutionWorkspaceSafetyFailureKind.IoFailure);
        }
    }

    private static bool IsEqualOrDescendant(
        string candidate,
        string root) =>
        string.Equals(
            candidate,
            root,
            StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith(
            root.TrimEnd('\\') + "\\",
            StringComparison.OrdinalIgnoreCase);

    private static ExecutionWorkspaceSafetyResult Invalid(
        ExecutionWorkspaceSafetyFailureKind kind) =>
        new(
            ExecutionWorkspaceSafetyStatus.Invalid,
            kind);
}
