function Get-MigrationReleaseMode {
    [CmdletBinding()]
    param(
        [AllowEmptyString()]
        [string]$RequestedMode = "",
        [Parameter(Mandatory)]
        [string]$Ref,
        [Parameter(Mandatory)]
        [string]$Version
    )

    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Release version must be numeric major.minor.patch."
    }
    $mode = if ([string]::IsNullOrWhiteSpace($RequestedMode)) { "unsigned" } else { $RequestedMode }
    if ($mode -cnotin @("unsigned", "signed")) {
        throw "Release signing mode must be unsigned or signed."
    }

    $isVersionTag = $Ref.StartsWith("refs/tags/v", [StringComparison]::Ordinal)
    if ($isVersionTag -and $Ref -cne "refs/tags/v$Version") {
        throw "Release tag does not match the central version."
    }

    $requireSigning = $isVersionTag -and $mode -ceq "signed"
    [pscustomobject]@{
        RequireSigning = $requireSigning
        EnvironmentName = if ($requireSigning) { "production-signing" } else { "release-dry-run" }
        ArtifactKind = if ($requireSigning) { "production-signed" }
            elseif ($isVersionTag) { "unsigned-community" }
            else { "unsigned-dry-run" }
    }
}
