using System.Windows;
using Microsoft.Win32;
using MinecraftInstanceMigration.App.Presentation;
using MinecraftInstanceMigration.Application.Inspection;
using MinecraftInstanceMigration.Application.Planning;
using MinecraftInstanceMigration.Infrastructure.Inspection;

namespace MinecraftInstanceMigration.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var inspectorModel = new InspectorViewModel(
            new InstanceInspector(new WindowsInspectionFileSystem()),
            () => ChooseFolder("Choose an instance candidate folder"));

        var previewModel = new MigrationPreviewViewModel(
            new InstanceInspector(new WindowsInspectionFileSystem()),
            new MigrationPlanner(),
            new MigrationPreviewer(),
            ChooseFolder);

        var model = new MainViewModel(inspectorModel, previewModel);
        DataContext = model;
        Closed += (_, _) => model.Cancel();
    }

    private string? ChooseFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }
}
