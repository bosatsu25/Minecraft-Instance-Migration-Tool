namespace MinecraftInstanceMigration.App.Presentation;

public sealed class MainViewModel(InspectorViewModel inspector, MigrationPreviewViewModel preview)
{
    public InspectorViewModel Inspector { get; } = inspector ?? throw new ArgumentNullException(nameof(inspector));

    public MigrationPreviewViewModel Preview { get; } = preview ?? throw new ArgumentNullException(nameof(preview));

    public void Cancel()
    {
        Inspector.Cancel();
        Preview.Cancel();
    }
}
