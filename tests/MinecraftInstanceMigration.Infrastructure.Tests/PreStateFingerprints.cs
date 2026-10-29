using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Infrastructure.Execution;

namespace MinecraftInstanceMigration.Infrastructure.Tests;

internal static class PreStateFingerprints
{
    // The fingerprint the pre-state probe proves for a Replace step: the destination entry as it is now.
    internal static ExecutionContentFingerprint Current(
        string root,
        ExecutionJournalEntry step)
    {
        using WindowsExecutionTree.HeldDirectory held =
            WindowsExecutionTree.OpenDirectoryChain(root, writableFinal: false);
        return WindowsExecutionTree.FingerprintNode(
            held.Root,
            step.Name,
            step.ExpectedKind,
            CancellationToken.None).ToDomain();
    }
}
