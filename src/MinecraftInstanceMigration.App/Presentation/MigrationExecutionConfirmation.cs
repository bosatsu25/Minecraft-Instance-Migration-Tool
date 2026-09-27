namespace MinecraftInstanceMigration.App.Presentation;

public sealed record MigrationExecutionConfirmation(
    string SourceRoot,
    string DestinationRoot,
    string SafetyWorkspace,
    int CopyCount,
    int ReplaceCount,
    int SkipCount,
    bool BackupRequired);

public interface IMigrationExecutionConfirmation
{
    bool Confirm(MigrationExecutionConfirmation request);
}

internal sealed class RejectingMigrationExecutionConfirmation : IMigrationExecutionConfirmation
{
    internal static RejectingMigrationExecutionConfirmation Instance { get; } = new();

    private RejectingMigrationExecutionConfirmation()
    {
    }

    public bool Confirm(MigrationExecutionConfirmation request) => false;
}
