namespace MinecraftInstanceMigration.App.Presentation;

public sealed record MigrationRollbackConfirmation(
    int RollbackCandidateCount,
    int DeleteCreatedEntryCount,
    int RestoreFromBackupCount,
    bool BackupRequired);

public interface IMigrationRollbackConfirmation
{
    bool Confirm(MigrationRollbackConfirmation request);
}

internal sealed class RejectingMigrationRollbackConfirmation : IMigrationRollbackConfirmation
{
    internal static RejectingMigrationRollbackConfirmation Instance { get; } = new();

    private RejectingMigrationRollbackConfirmation()
    {
    }

    public bool Confirm(MigrationRollbackConfirmation request) => false;
}
