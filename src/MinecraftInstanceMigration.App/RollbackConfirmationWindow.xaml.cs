using System.Windows;
using MinecraftInstanceMigration.App.Presentation;

namespace MinecraftInstanceMigration.App;

public partial class RollbackConfirmationWindow : Window
{
    public RollbackConfirmationWindow(MigrationRollbackConfirmation confirmation)
    {
        InitializeComponent();
        DataContext = confirmation ?? throw new ArgumentNullException(nameof(confirmation));
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
