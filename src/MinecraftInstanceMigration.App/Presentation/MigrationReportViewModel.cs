using System.ComponentModel;
using System.Runtime.CompilerServices;
using MinecraftInstanceMigration.Application.Reporting;

namespace MinecraftInstanceMigration.App.Presentation;

public sealed class MigrationReportViewModel(
    IMigrationReportProjector projector) : INotifyPropertyChanged
{
    private readonly IMigrationReportProjector projector =
        projector ?? throw new ArgumentNullException(nameof(projector));
    private MigrationReport? report;
    private string status = "No migration result is available yet.";

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool HasReport => report is not null;

    public bool HasRecovery => report?.Recovery is not null;

    public string OverallOutcome => report?.OverallOutcome.ToString() ?? "Not available";

    public string Status => status;

    public string MigrationSummary => report is null
        ? ""
        : $"Selected: {report.Migration.SelectedEntryCount}; Copy: {report.Migration.CopyCount}; " +
          $"Replace: {report.Migration.ReplaceCount}; Skip: {report.Migration.SkipCount}; " +
          $"Excluded: {report.Migration.ExcludedCount}; source missing: {report.Migration.SourceMissingCount}; " +
          $"blocked: {report.Migration.BlockedCount}. Rules: " +
          string.Join(" ", report.Migration.ContentRuleSummaries);

    public string ExecutionSummary => report is null
        ? ""
        : $"Planned writes: {report.Execution.PlannedWriteCount}; Applied: {report.Execution.AppliedCount}; " +
          $"Failed: {report.Execution.FailedCount}; Uncertain: {report.Execution.UncertainCount}; " +
          $"verification: {report.Execution.VerificationOutcome}.";

    public string BackupSummary => report is null
        ? ""
        : $"Backup: {report.Backup.Outcome}; Replace entries: {report.Backup.ReplaceEntryCount}.";

    public string RecoverySummary => report?.Recovery is null
        ? ""
        : $"Diagnosis: {report.Recovery.DiagnosisStatus}; candidates: {report.Recovery.RollbackCandidateCount}; " +
          $"delete created: {report.Recovery.DeleteCreatedEntryCount}; restore backup: {report.Recovery.RestoreFromBackupCount}; " +
          $"rollback: {report.Recovery.RollbackOutcome?.ToString() ?? "Not started"}; " +
          $"Applied: {Count(report.Recovery.AppliedCount)}; GuardRejected: {Count(report.Recovery.GuardRejectedCount)}; " +
          $"Failed: {Count(report.Recovery.FailedCount)}; Uncertain: {Count(report.Recovery.UncertainCount)}.";

    public void Publish(MigrationReportEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        try
        {
            MigrationReportCreationResult result = projector.Create(evidence);
            if (!result.IsCreated || result.Report is null)
            {
                report = null;
                status = result.FailureKind is null
                    ? "Migration report is unavailable because evidence could not be validated."
                    : $"Migration report is unavailable ({result.FailureKind}). The migration result is unchanged.";
            }
            else
            {
                report = result.Report;
                status = "Report generated from existing read-only evidence. No workflow state was changed.";
            }
        }
        catch (Exception)
        {
            report = null;
            status = "Migration report generation failed. The migration result is unchanged; technical details are not displayed.";
        }

        NotifyAll();
    }

    public void Clear()
    {
        report = null;
        status = "No migration result is available yet.";
        NotifyAll();
    }

    private void NotifyAll()
    {
        Notify(nameof(HasReport));
        Notify(nameof(HasRecovery));
        Notify(nameof(OverallOutcome));
        Notify(nameof(Status));
        Notify(nameof(MigrationSummary));
        Notify(nameof(ExecutionSummary));
        Notify(nameof(BackupSummary));
        Notify(nameof(RecoverySummary));
    }

    private static string Count(int? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown";

    private void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
