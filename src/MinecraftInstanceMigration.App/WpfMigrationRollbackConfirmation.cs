using System.Windows;
using MinecraftInstanceMigration.App.Presentation;

namespace MinecraftInstanceMigration.App;

public sealed class WpfMigrationRollbackConfirmation(Window owner)
    : IMigrationRollbackConfirmation
{
    private readonly Window owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public bool Confirm(MigrationRollbackConfirmation request) =>
        new RollbackConfirmationWindow(request)
        {
            Owner = owner,
        }.ShowDialog() == true;
}
