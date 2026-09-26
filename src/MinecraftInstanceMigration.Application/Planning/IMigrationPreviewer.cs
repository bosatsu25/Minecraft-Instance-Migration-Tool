using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Planning;

public interface IMigrationPreviewer
{
    MigrationPreview CreatePreview(MigrationPlan plan);
}
