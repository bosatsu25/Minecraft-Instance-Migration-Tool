using System.Windows;
using MinecraftInstanceMigration.App.Presentation;

namespace MinecraftInstanceMigration.App;

public sealed class WpfMigrationExecutionConfirmation(Window owner)
    : IMigrationExecutionConfirmation
{
    private readonly Window owner = owner ?? throw new ArgumentNullException(nameof(owner));

    public bool Confirm(MigrationExecutionConfirmation request) =>
        new MigrationConfirmationWindow(request)
        {
            Owner = owner,
        }.ShowDialog() == true;
}
