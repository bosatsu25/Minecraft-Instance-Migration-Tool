namespace MinecraftInstanceMigration.Application.Reporting;

public interface IMigrationReportProjector
{
    MigrationReportCreationResult Create(MigrationReportEvidence evidence);
}
