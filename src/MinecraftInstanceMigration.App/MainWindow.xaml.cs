using System.Windows;
using Microsoft.Win32;
using MinecraftInstanceMigration.App.Presentation;
using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Application.Inspection;
using MinecraftInstanceMigration.Application.Planning;
using MinecraftInstanceMigration.Application.Workflow;
using MinecraftInstanceMigration.Infrastructure.Backup;
using MinecraftInstanceMigration.Infrastructure.Execution;
using MinecraftInstanceMigration.Infrastructure.Inspection;

namespace MinecraftInstanceMigration.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var inspector = new InstanceInspector(new WindowsInspectionFileSystem());
        var backupPlanner = new BackupPlanner();
        var backupStorage = new WindowsBackupStorage();

        var workflow = new MigrationWorkflow(
            inspector,
            new MigrationPlanner(),
            new MigrationPreviewer(),
            backupPlanner,
            new BackupExecutor(backupStorage, backupPlanner),
            new ExecutionOrchestrator(
                new ExecutionSafetyPlanner(),
                new ExecutionLiveValidator(inspector),
                backupPlanner,
                new BackupArtifactValidator(backupStorage),
                new WindowsExecutionWorkspaceSafetyValidator(),
                new ExecutionJournalPersistence(new WindowsExecutionJournalStorage()),
                new WindowsExecutionMutationPort(),
                new WindowsExecutionPostWriteVerifier()));

        var inspectorModel = new InspectorViewModel(
            inspector,
            () => ChooseFolder("Choose an instance candidate folder"));

        var previewModel = new MigrationPreviewViewModel(
            workflow,
            ChooseFolder,
            new WpfMigrationExecutionConfirmation(this));

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
