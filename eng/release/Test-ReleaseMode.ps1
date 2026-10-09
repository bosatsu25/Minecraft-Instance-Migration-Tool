[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "ReleaseMode.ps1")

function Assert-Mode([string]$Requested, [string]$Ref, [bool]$Signing, [string]$Environment, [string]$Kind) {
    $actual = Get-MigrationReleaseMode -RequestedMode $Requested -Ref $Ref -Version "1.0.0"
    if ($actual.RequireSigning -ne $Signing -or $actual.EnvironmentName -cne $Environment -or
        $actual.ArtifactKind -cne $Kind) {
        throw "Release mode selected an unexpected signing, environment, or artifact contract."
    }
}

Assert-Mode "" "refs/tags/v1.0.0" $false "release-dry-run" "unsigned-community"
Assert-Mode "unsigned" "refs/tags/v1.0.0" $false "release-dry-run" "unsigned-community"
Assert-Mode "signed" "refs/tags/v1.0.0" $true "production-signing" "production-signed"
Assert-Mode "signed" "refs/pull/1/merge" $false "release-dry-run" "unsigned-dry-run"
Assert-Mode "signed" "refs/heads/main" $false "release-dry-run" "unsigned-dry-run"

foreach ($invalid in @(
    @{ Mode = "automatic"; Ref = "refs/tags/v1.0.0"; Version = "1.0.0" },
    @{ Mode = "unsigned"; Ref = "refs/tags/v2.0.0"; Version = "1.0.0" },
    @{ Mode = "unsigned"; Ref = "refs/tags/v1.0.0-rc.1"; Version = "1.0.0" },
    @{ Mode = "signed"; Ref = "refs/tags/v1.0.0"; Version = "1.0.0-rc.1" }
)) {
    $rejected = $false
    try {
        $null = Get-MigrationReleaseMode -RequestedMode $invalid.Mode -Ref $invalid.Ref -Version $invalid.Version
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw "Invalid release mode or tag/version combination was accepted."
    }
}

Write-Host "Release-mode contract passed: 5 routing cases and 4 rejection cases."
