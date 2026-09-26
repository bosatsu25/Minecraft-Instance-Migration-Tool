using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Planning;

public sealed class MigrationPreviewer : IMigrationPreviewer
{
    public MigrationPreview CreatePreview(MigrationPlan plan) =>
        MigrationPreviewPolicy.Create(plan);
}
