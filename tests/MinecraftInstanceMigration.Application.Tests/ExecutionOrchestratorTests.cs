using MinecraftInstanceMigration.Application.Backup;
using MinecraftInstanceMigration.Application.Execution;
using MinecraftInstanceMigration.Domain.Backup;
using MinecraftInstanceMigration.Domain.Execution;
using MinecraftInstanceMigration.Domain.Inspection;
using MinecraftInstanceMigration.Domain.Planning;

namespace MinecraftInstanceMigration.Application.Tests;

public sealed class ExecutionOrchestratorTests
{
    [Fact]
    public async Task ReplaceFlowOrdersRevalidationBackupJournalMutationVerificationAndApplied()
    {
        var events = new List<string>();
        Fixture fixture = CreateFixture(events, ReplacePlan());

        ExecutionOrchestrationResult result = await fixture.Orchestrator.ExecuteAsync(
            Request(ReplacePlan(), backupRoot: "backup"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOrchestrationStatus.Completed, result.Status);
        Assert.Equal(1, result.AppliedSteps);
        Assert.Equal(
            new[]
            {
                "workspace",
                "journal:create",
                "live:0",
                "backup",
                "journal:started:0",
                "mutation:0",
                "verify:0",
                "journal:applied:0",
            },
            events);
    }


    [Fact]
    public async Task UnsafeWorkspaceBlocksBeforeJournalCreation()
    {
        var events = new List<string>();
        MigrationPlan plan = CopyPlan();
        Fixture fixture = CreateFixture(events, plan);
        fixture.Workspace.Result = new ExecutionWorkspaceSafetyResult(
            ExecutionWorkspaceSafetyStatus.Invalid,
            ExecutionWorkspaceSafetyFailureKind.JournalInsideMigrationRoot);

        ExecutionOrchestrationResult result = await fixture.Orchestrator.ExecuteAsync(
            Request(plan, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOrchestrationStatus.Blocked, result.Status);
        Assert.Equal(
            ExecutionOrchestrationFailureKind.UnsafeWorkspace,
            result.FailureKind);
        Assert.Equal(new[] { "workspace" }, events);
    }

    [Fact]
    public async Task CopyFlowDoesNotRequireBackupValidation()
    {
        var events = new List<string>();
        MigrationPlan plan = CopyPlan();
        Fixture fixture = CreateFixture(events, plan);

        ExecutionOrchestrationResult result = await fixture.Orchestrator.ExecuteAsync(
            Request(plan, backupRoot: null),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOrchestrationStatus.Completed, result.Status);
        Assert.DoesNotContain("backup", events);
    }

    [Fact]
    public async Task LiveStateMismatchBlocksBeforeStartedAndMutation()
    {
        var events = new List<string>();
        MigrationPlan plan = ReplacePlan();
        Fixture fixture = CreateFixture(events, plan);
        fixture.Live.Result = new ExecutionLiveValidationResult(
            ExecutionLiveValidationStatus.Invalid,
            [new ExecutionLiveValidationIssue(
                ExecutionLiveValidationIssueKind.DestinationStateChanged,
                "config")]);

        ExecutionOrchestrationResult result = await fixture.Orchestrator.ExecuteAsync(
            Request(plan, "backup"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOrchestrationStatus.Blocked, result.Status);
        Assert.Equal(ExecutionOrchestrationFailureKind.LiveStateChanged, result.FailureKind);
        Assert.DoesNotContain(events, entry => entry.StartsWith("journal:started", StringComparison.Ordinal));
        Assert.DoesNotContain(events, entry => entry.StartsWith("mutation:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidBackupBlocksReplaceBeforeStarted()
    {
        var events = new List<string>();
        MigrationPlan plan = ReplacePlan();
        Fixture fixture = CreateFixture(events, plan);
        fixture.Backup.Result = new BackupArtifactValidationResult(
            BackupArtifactValidationStatus.Invalid,
            BackupArtifactFailureKind.VerificationMismatch);

        ExecutionOrchestrationResult result = await fixture.Orchestrator.ExecuteAsync(
            Request(plan, "backup"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOrchestrationStatus.Blocked, result.Status);
        Assert.Equal(ExecutionOrchestrationFailureKind.BackupInvalid, result.FailureKind);
        Assert.DoesNotContain(events, entry => entry.StartsWith("journal:started", StringComparison.Ordinal));
        Assert.DoesNotContain(events, entry => entry.StartsWith("mutation:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartedMustBeDurableBeforeMutation()
    {
        var events = new List<string>();
        MigrationPlan plan = CopyPlan();
        Fixture fixture = CreateFixture(events, plan);
        fixture.Journal.StartedResult = new ExecutionJournalWriteResult(
            ExecutionJournalWriteStatus.Failed,
            fixture.Journal.Reference,
            ExecutionJournalPersistenceFailureKind.IoFailure);

        ExecutionOrchestrationResult result = await fixture.Orchestrator.ExecuteAsync(
            Request(plan, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOrchestrationStatus.Blocked, result.Status);
        Assert.Equal(ExecutionOrchestrationFailureKind.JournalFailure, result.FailureKind);
        Assert.DoesNotContain(events, entry => entry.StartsWith("mutation:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MutationFailureDurablyMarksFailedAndRequiresRecovery()
    {
        var events = new List<string>();
        MigrationPlan plan = CopyPlan();
        Fixture fixture = CreateFixture(events, plan);
        fixture.Mutation.Result = new ExecutionMutationResult(ExecutionMutationStatus.Failed);

        ExecutionOrchestrationResult result = await fixture.Orchestrator.ExecuteAsync(
            Request(plan, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOrchestrationStatus.RecoveryRequired, result.Status);
        Assert.Equal(ExecutionOrchestrationFailureKind.MutationFailed, result.FailureKind);
        Assert.Contains("journal:failed:0", events);
        Assert.DoesNotContain(events, entry => entry.StartsWith("verify:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerificationFailureDurablyMarksFailedAndRequiresRecovery()
    {
        var events = new List<string>();
        MigrationPlan plan = CopyPlan();
        Fixture fixture = CreateFixture(events, plan);
        fixture.Verifier.Result = new ExecutionPostWriteVerificationResult(
            ExecutionPostWriteVerificationStatus.Failed);

        ExecutionOrchestrationResult result = await fixture.Orchestrator.ExecuteAsync(
            Request(plan, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOrchestrationStatus.RecoveryRequired, result.Status);
        Assert.Equal(
            ExecutionOrchestrationFailureKind.PostWriteVerificationFailed,
            result.FailureKind);
        Assert.Contains("journal:failed:0", events);
        Assert.DoesNotContain(events, entry => entry.StartsWith("journal:applied", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AppliedJournalFailureLeavesStartedEvidenceAndRequiresRecovery()
    {
        var events = new List<string>();
        MigrationPlan plan = CopyPlan();
        Fixture fixture = CreateFixture(events, plan);
        fixture.Journal.AppliedResult = new ExecutionJournalWriteResult(
            ExecutionJournalWriteStatus.Failed,
            fixture.Journal.Reference,
            ExecutionJournalPersistenceFailureKind.IoFailure);

        ExecutionOrchestrationResult result = await fixture.Orchestrator.ExecuteAsync(
            Request(plan, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOrchestrationStatus.RecoveryRequired, result.Status);
        Assert.Equal(ExecutionOrchestrationFailureKind.JournalFailure, result.FailureKind);
        Assert.Contains("mutation:0", events);
        Assert.Contains("verify:0", events);
    }

    [Fact]
    public async Task CancellationAfterOneAppliedStepStopsBeforeSecondStarted()
    {
        var events = new List<string>();
        MigrationPlan plan = TwoCopyPlan();
        using var cancellation = new CancellationTokenSource();
        Fixture fixture = CreateFixture(events, plan);
        fixture.Journal.OnApplied = sequence =>
        {
            if (sequence == 0)
            {
                cancellation.Cancel();
            }
        };

        ExecutionOrchestrationResult result = await fixture.Orchestrator.ExecuteAsync(
            Request(plan, null),
            cancellation.Token);

        Assert.Equal(ExecutionOrchestrationStatus.Cancelled, result.Status);
        Assert.Equal(1, result.AppliedSteps);
        Assert.Contains("journal:applied:0", events);
        Assert.DoesNotContain("journal:started:1", events);
        Assert.DoesNotContain("mutation:1", events);
    }

    [Fact]
    public async Task RevalidationFailureAfterPriorAppliedStepRequiresRecovery()
    {
        var events = new List<string>();
        MigrationPlan plan = TwoCopyPlan();
        Fixture fixture = CreateFixture(events, plan);
        fixture.Live.OnValidate = step =>
            step.Sequence == 0
                ? Valid()
                : new ExecutionLiveValidationResult(
                    ExecutionLiveValidationStatus.Invalid,
                    [new ExecutionLiveValidationIssue(
                        ExecutionLiveValidationIssueKind.DestinationStateChanged,
                        step.Name)]);

        ExecutionOrchestrationResult result = await fixture.Orchestrator.ExecuteAsync(
            Request(plan, null),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionOrchestrationStatus.RecoveryRequired, result.Status);
        Assert.Equal(1, result.AppliedSteps);
        Assert.Equal(ExecutionOrchestrationFailureKind.LiveStateChanged, result.FailureKind);
        Assert.DoesNotContain("journal:started:1", events);
    }

    private static Fixture CreateFixture(
        List<string> events,
        MigrationPlan plan)
    {
        var live = new StubLive(events);
        var backup = new StubBackupValidator(events);
        var workspace = new StubWorkspace(events);
        var journal = new StubJournal(events);
        var mutation = new StubMutation(events);
        var verifier = new StubVerifier(events);

        var orchestrator = new ExecutionOrchestrator(
            new ExecutionSafetyPlanner(),
            live,
            new BackupPlanner(),
            backup,
            workspace,
            journal,
            mutation,
            verifier);

        return new Fixture(
            orchestrator,
            live,
            backup,
            workspace,
            journal,
            mutation,
            verifier);
    }

    private static ExecutionOrchestrationRequest Request(
        MigrationPlan plan,
        string? backupRoot) =>
        new(
            "source",
            "destination",
            "journals",
            backupRoot,
            plan);

    private static MigrationPlan CopyPlan() =>
        new(
            EntryState.Directory,
            EntryState.Directory,
            [
                Entry(
                    "options.txt",
                    ExpectedEntryKind.File,
                    EntryState.File,
                    EntryState.Missing,
                    MigrationPlanDisposition.ReadyToCopy),
            ],
            []);

    private static MigrationPlan TwoCopyPlan() =>
        new(
            EntryState.Directory,
            EntryState.Directory,
            [
                Entry(
                    "options.txt",
                    ExpectedEntryKind.File,
                    EntryState.File,
                    EntryState.Missing,
                    MigrationPlanDisposition.ReadyToCopy),
                Entry(
                    "config",
                    ExpectedEntryKind.Directory,
                    EntryState.Directory,
                    EntryState.Missing,
                    MigrationPlanDisposition.ReadyToCopy),
            ],
            []);

    private static MigrationPlan ReplacePlan() =>
        new(
            EntryState.Directory,
            EntryState.Directory,
            [
                Entry(
                    "config",
                    ExpectedEntryKind.Directory,
                    EntryState.Directory,
                    EntryState.Directory,
                    MigrationPlanDisposition.ReadyToReplace),
            ],
            []);

    private static MigrationPlanEntry Entry(
        string name,
        ExpectedEntryKind kind,
        EntryState source,
        EntryState destination,
        MigrationPlanDisposition disposition) =>
        new(name, kind, source, destination, true, disposition);

    private static ExecutionLiveValidationResult Valid() =>
        new(ExecutionLiveValidationStatus.Valid, []);

    private sealed record Fixture(
        ExecutionOrchestrator Orchestrator,
        StubLive Live,
        StubBackupValidator Backup,
        StubWorkspace Workspace,
        StubJournal Journal,
        StubMutation Mutation,
        StubVerifier Verifier);

    private sealed class StubLive(List<string> events) : IExecutionLiveValidator
    {
        public ExecutionLiveValidationResult Result { get; set; } = Valid();

        public Func<ExecutionJournalEntry, ExecutionLiveValidationResult>? OnValidate { get; set; }

        public Task<ExecutionLiveValidationResult> ValidateStepAsync(
            string sourceRoot,
            string destinationRoot,
            MigrationPlan migrationPlan,
            ExecutionJournalEntry step,
            CancellationToken cancellationToken = default)
        {
            events.Add($"live:{step.Sequence}");
            return Task.FromResult(OnValidate?.Invoke(step) ?? Result);
        }
    }

    private sealed class StubBackupValidator(List<string> events) : IBackupArtifactValidator
    {
        public BackupArtifactValidationResult Result { get; set; } =
            new(
                BackupArtifactValidationStatus.Valid,
                Verification: new BackupVerificationSummary(
                    1,
                    1,
                    10,
                    new string('A', 64)));

        public Task<BackupArtifactValidationResult> ValidateAsync(
            string backupRoot,
            BackupPlan plan,
            CancellationToken cancellationToken = default)
        {
            events.Add("backup");
            return Task.FromResult(Result);
        }
    }


    private sealed class StubWorkspace(List<string> events)
        : IExecutionWorkspaceSafetyValidator
    {
        public ExecutionWorkspaceSafetyResult Result { get; set; } =
            new(ExecutionWorkspaceSafetyStatus.Safe);

        public Task<ExecutionWorkspaceSafetyResult> ValidateAsync(
            string sourceRoot,
            string destinationRoot,
            string journalParent,
            CancellationToken cancellationToken = default)
        {
            events.Add("workspace");
            return Task.FromResult(Result);
        }
    }

    private sealed class StubJournal : IExecutionJournalPersistence
    {
        private readonly List<string> events;

        public ExecutionJournalReference Reference { get; } =
            new(
                "0123456789abcdef0123456789abcdef",
                @"C:\journals\mim-journal-0123456789abcdef0123456789abcdef.jsonl");

        public ExecutionJournalWriteResult StartedResult { get; set; }

        public ExecutionJournalWriteResult AppliedResult { get; set; }

        public Action<int>? OnApplied { get; set; }

        public StubJournal(List<string> events)
        {
            this.events = events;
            StartedResult = new ExecutionJournalWriteResult(
                ExecutionJournalWriteStatus.Succeeded,
                Reference);
            AppliedResult = new ExecutionJournalWriteResult(
                ExecutionJournalWriteStatus.Succeeded,
                Reference);
        }

        public Task<ExecutionJournalWriteResult> CreateAsync(
            string journalParent,
            ExecutionJournalDraft draft,
            CancellationToken cancellationToken = default)
        {
            events.Add("journal:create");
            return Task.FromResult(new ExecutionJournalWriteResult(
                ExecutionJournalWriteStatus.Succeeded,
                Reference));
        }

        public Task<ExecutionJournalWriteResult> MarkStepStartedAsync(
            ExecutionJournalReference journal,
            ExecutionJournalDraft draft,
            int sequence,
            CancellationToken cancellationToken = default)
        {
            events.Add($"journal:started:{sequence}");
            return Task.FromResult(StartedResult);
        }

        public Task<ExecutionJournalWriteResult> MarkStepAppliedAsync(
            ExecutionJournalReference journal,
            ExecutionJournalDraft draft,
            int sequence,
            ExecutionContentFingerprint fingerprint,
            CancellationToken cancellationToken = default)
        {
            events.Add($"journal:applied:{sequence}");
            OnApplied?.Invoke(sequence);
            return Task.FromResult(AppliedResult);
        }

        public Task<ExecutionJournalWriteResult> MarkStepFailedAsync(
            ExecutionJournalReference journal,
            ExecutionJournalDraft draft,
            int sequence,
            CancellationToken cancellationToken = default)
        {
            events.Add($"journal:failed:{sequence}");
            return Task.FromResult(new ExecutionJournalWriteResult(
                ExecutionJournalWriteStatus.Succeeded,
                Reference));
        }

        public Task<ExecutionJournalReadResult> LoadAsync(
            ExecutionJournalReference journal,
            ExecutionJournalDraft draft,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubMutation(List<string> events) : IExecutionMutationPort
    {
        public ExecutionMutationResult Result { get; set; } =
            new(ExecutionMutationStatus.Applied);

        public Task<ExecutionMutationResult> ApplyAsync(
            string sourceRoot,
            string destinationRoot,
            ExecutionJournalEntry step,
            CancellationToken cancellationToken)
        {
            events.Add($"mutation:{step.Sequence}");
            return Task.FromResult(Result);
        }
    }

    private sealed class StubVerifier(List<string> events) : IExecutionPostWriteVerifier
    {
        public ExecutionPostWriteVerificationResult Result { get; set; } =
            new(
                ExecutionPostWriteVerificationStatus.Verified,
                new ExecutionContentFingerprint(
                    1,
                    0,
                    10,
                    new string('B', 64)));

        public Task<ExecutionPostWriteVerificationResult> VerifyAsync(
            string sourceRoot,
            string destinationRoot,
            ExecutionJournalEntry step,
            CancellationToken cancellationToken)
        {
            events.Add($"verify:{step.Sequence}");
            return Task.FromResult(Result);
        }
    }
}
