using System.Windows;
using MinecraftInstanceMigration.App.Presentation;

namespace MinecraftInstanceMigration.App;

public partial class MigrationConfirmationWindow : Window
{
    public MigrationConfirmationWindow(MigrationExecutionConfirmation request)
    {
        ArgumentNullException.ThrowIfNull(request);
        InitializeComponent();
        DataContext = request;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
