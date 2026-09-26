namespace MinecraftInstanceMigration.Application.Backup;

public enum BackupArtifactValidationStatus
{
    Valid,
    Invalid,
    Cancelled,
}

public enum BackupArtifactFailureKind
{
    InvalidPlan,
    InvalidPath,
    ReparsePoint,
    OwnershipMarkerInvalid,
    ManifestInvalid,
    UnexpectedContent,
    VerificationMismatch,
    AccessDenied,
    IoFailure,
}

public sealed record BackupArtifactValidationResult(
    BackupArtifactValidationStatus Status,
    BackupArtifactFailureKind? FailureKind = null,
    BackupVerificationSummary? Verification = null)
{
    public bool IsValid => Status == BackupArtifactValidationStatus.Valid;
}
