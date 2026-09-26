using System.Windows;
using Microsoft.Win32;
using MinecraftInstanceMigration.App.Presentation;
using MinecraftInstanceMigration.Application.Inspection;
using MinecraftInstanceMigration.Infrastructure.Inspection;

namespace MinecraftInstanceMigration.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var model = new InspectorViewModel(new InstanceInspector(new WindowsInspectionFileSystem()), ChooseFolder);
        DataContext = model;
        Closed += (_, _) => model.Cancel();
    }

    private string? ChooseFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose an instance candidate folder", Multiselect = false };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }
}
