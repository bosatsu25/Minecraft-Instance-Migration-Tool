using System.Windows;
using Microsoft.Win32;
using MinecraftInstanceMigration.App.Presentation;
using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Application.Capacity;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Application.Inspection;
using MinecraftInstanceMigration.Application.Planning;
using MinecraftInstanceMigration.Application.Reporting;
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
        var backupValidator = new BackupArtifactValidator(backupStorage);
        var capacityProbe = new WindowsMigrationCapacityProbe();
        var safetyPlanner = new ExecutionSafetyPlanner();
        var journalPersistence = new ExecutionJournalPersistence(new WindowsExecutionJournalStorage());
        var rollbackAttemptPersistence = new RollbackAttemptPersistence(new WindowsRollbackAttemptStorage());
        var rollbackExecutor = new RollbackExecutor(
            backupValidator,
            rollbackAttemptPersistence,
            new WindowsRollbackStorage());
        var recoveryCoordinator = new MigrationRecoveryCoordinator(
            safetyPlanner,
            journalPersistence,
            backupValidator,
            rollbackExecutor,
            rollbackAttemptPersistence);

        var workflow = new MigrationWorkflow(
            inspector,
            new MigrationPlanner(),
            new MigrationPreviewer(),
            new MigrationCapacityPreflight(capacityProbe, capacityProbe),
            backupPlanner,
            new BackupExecutor(backupStorage, backupPlanner),
            new ExecutionOrchestrator(
                safetyPlanner,
                new ExecutionLiveValidator(inspector),
                backupPlanner,
                backupValidator,
                new WindowsExecutionWorkspaceSafetyValidator(),
                journalPersistence,
                new WindowsExecutionMutationPort(),
                new WindowsExecutionPostWriteVerifier()));

        var inspectorModel = new InspectorViewModel(
            inspector,
            () => ChooseFolder("Choose an instance candidate folder"));

        var reportModel = new MigrationReportViewModel(new MigrationReportProjector());
        var previewModel = new MigrationPreviewViewModel(
            workflow,
            ChooseFolder,
            new WpfMigrationExecutionConfirmation(this),
            recoveryCoordinator,
            new WpfMigrationRollbackConfirmation(this),
            reportModel);

        var model = new MainViewModel(inspectorModel, previewModel, reportModel);
        DataContext = model;
        Closed += (_, _) => model.Cancel();
    }

    private string? ChooseFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }
}
