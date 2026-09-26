using MinecraftInstanceMigration.Application.Planning;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Tests;

public sealed class MigrationPreviewerTests
{
    [Fact]
    public void ApplicationPreviewerProjectsExistingPlanWithoutReinspection()
    {
        var plan = new MigrationPlan(
            EntryState.Directory,
            EntryState.Directory,
            [
                new MigrationPlanEntry(
                    "config",
                    ExpectedEntryKind.Directory,
                    EntryState.Directory,
                    EntryState.Missing,
                    true,
                    MigrationPlanDisposition.ReadyToCopy),
            ],
            []);

        MigrationPreview preview = new MigrationPreviewer().CreatePreview(plan);

        MigrationPreviewEntry entry = Assert.Single(preview.Entries);
        Assert.Equal(MigrationPreviewAction.Copy, entry.Action);
        Assert.Equal(MigrationPlanStatus.Ready, preview.Status);
    }
}
