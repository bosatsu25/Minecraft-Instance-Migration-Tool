using MinecraftInstanceMigration.Domain.Rules;

namespace MinecraftInstanceMigration.Domain.Planning;

public sealed class MigrationPreview
{
    private static readonly IReadOnlyList<string> KnownContentRuleSummaries =
        Array.AsReadOnly(new[]
        {
            KnownMigrationContentRules.HanemodClientExclusionSummary,
        });

    public MigrationPreview(
        MigrationPlanStatus status,
        IEnumerable<MigrationPreviewEntry> entries,
        IEnumerable<string> unknownSelections,
        IEnumerable<ConflictDecisionIssue> conflictDecisionIssues)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(unknownSelections);
        ArgumentNullException.ThrowIfNull(conflictDecisionIssues);

        Status = status;
        Entries = Array.AsReadOnly(entries.ToArray());
        UnknownSelections = Array.AsReadOnly(unknownSelections.ToArray());
        ConflictDecisionIssues = Array.AsReadOnly(conflictDecisionIssues.ToArray());
    }

    public MigrationPlanStatus Status { get; }

    public IReadOnlyList<MigrationPreviewEntry> Entries { get; }

    public IReadOnlyList<string> UnknownSelections { get; }

    public IReadOnlyList<ConflictDecisionIssue> ConflictDecisionIssues { get; }

    public IReadOnlyList<string> ContentRuleSummaries => KnownContentRuleSummaries;

    public int CopyCount => Entries.Count(entry => entry.Action == MigrationPreviewAction.Copy);

    public int ReplaceCount => Entries.Count(entry => entry.Action == MigrationPreviewAction.Replace);

    public int SkipCount => Entries.Count(entry => entry.Action == MigrationPreviewAction.Skip);

    public int NoSourceCount => Entries.Count(entry => entry.Action == MigrationPreviewAction.NoSource);

    public int NeedsDecisionCount => Entries.Count(entry => entry.Action == MigrationPreviewAction.NeedsDecision);

    public int BlockedCount => Entries.Count(entry => entry.Action == MigrationPreviewAction.Blocked);

    public bool RequiresBackup => Entries.Any(entry => entry.RequiresBackup);

    public bool HasPlannedWrites => Entries.Any(entry =>
        entry.Action is MigrationPreviewAction.Copy or MigrationPreviewAction.Replace);
}
