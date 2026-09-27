namespace MinecraftInstanceMigration.App.Presentation;

public sealed class MainViewModel(
    InspectorViewModel inspector,
    MigrationPreviewViewModel preview,
    MigrationReportViewModel report)
{
    public InspectorViewModel Inspector { get; } = inspector ?? throw new ArgumentNullException(nameof(inspector));

    public MigrationPreviewViewModel Preview { get; } = preview ?? throw new ArgumentNullException(nameof(preview));

    public MigrationReportViewModel Report { get; } = report ?? throw new ArgumentNullException(nameof(report));

    public void Cancel()
    {
        Inspector.Cancel();
        Preview.Cancel();
    }
}
