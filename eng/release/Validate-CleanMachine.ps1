[CmdletBinding()]
param(
    [string]$ArtifactsRoot = (Join-Path $PSScriptRoot "..\..\artifacts"),
    [string]$ExpectedVersion,
    [switch]$RequireSigning
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-ReleaseVersion {
    $propsPath = Join-Path (Split-Path -Path $PSScriptRoot -Parent | Split-Path -Parent) "Directory.Build.props"
    [xml]$props = Get-Content -LiteralPath $propsPath -Raw
    $node = @($props.SelectNodes('/Project/PropertyGroup/Version'))
    if ($node.Count -ne 1) {
        throw "Directory.Build.props must contain exactly one Version value."
    }

    $version = [string]$node[0].InnerText
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "The central Version must be a numeric major.minor.patch value."
    }

    return $version
}

function Assert-PathExists([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not found: $Path"
    }
}

function New-TemporaryFixtureRoot([string]$RootName) {
    $root = [System.IO.Path]::GetFullPath((Join-Path $env:TEMP $RootName))
    if (Test-Path -LiteralPath $root) {
        throw "Clean-machine validation refused to reuse a temporary fixture directory: $root"
    }

    [void](New-Item -ItemType Directory -Path $root -Force)
    return $root
}

function Invoke-PortableLaunch([string]$ExecutablePath, [string]$Surface) {
    $process = Start-Process -FilePath $ExecutablePath -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        while (-not $process.HasExited -and $process.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 200
            $process.Refresh()
        }

        if ($process.HasExited) {
            throw "$Surface exited before opening its main window."
        }
        if ($process.MainWindowHandle -eq [IntPtr]::Zero) {
            throw "$Surface did not open the main window."
        }
    }
    finally {
        if (-not $process.HasExited) {
            [void]$process.CloseMainWindow()
            if (-not $process.WaitForExit(5000)) {
                Stop-Process -Id $process.Id -Force
                $process.WaitForExit()
            }
        }

        $process.Dispose()
    }
}

$artifactRoot = [System.IO.Path]::GetFullPath($ArtifactsRoot)
$packageDirectory = Join-Path $artifactRoot "release"
$expectedVersion = if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) { Get-ReleaseVersion } else { $ExpectedVersion }

$portableName = "MinecraftInstanceMigrationTool-$expectedVersion-win-x64.zip"
$installerName = "MinecraftInstanceMigrationTool-$expectedVersion-win-x64-setup.exe"
$checksumsName = "SHA256SUMS.txt"
$portablePath = Join-Path $packageDirectory $portableName
$installerPath = Join-Path $packageDirectory $installerName
$checksumsPath = Join-Path $packageDirectory $checksumsName

Assert-PathExists $portablePath "Portable ZIP"
Assert-PathExists $installerPath "Installer"
Assert-PathExists $checksumsPath "SHA256SUMS.txt"

& (Join-Path $PSScriptRoot "Verify-Release.ps1") -ArtifactsRoot $artifactRoot -ExpectedVersion $expectedVersion -RequireSigning:$RequireSigning
$portableValidationRoot = New-TemporaryFixtureRoot ("mim-portable-validation-" + [Guid]::NewGuid().ToString("N"))

try {
    [System.IO.Compression.ZipFile]::ExtractToDirectory($portablePath, $portableValidationRoot)
    $portableExecutable = Join-Path $portableValidationRoot "MinecraftInstanceMigrationTool.exe"
    if (-not (Test-Path -LiteralPath $portableExecutable -PathType Leaf)) {
        throw "The extracted portable package did not contain the expected executable."
    }

    # Direct launch validation for the portable package.
    Invoke-PortableLaunch $portableExecutable "portable ZIP direct launch"

    Write-Host "Clean-machine package validation passed for version $expectedVersion; artifact verification and portable ZIP launch completed."
}
finally {
    if (Test-Path -LiteralPath $portableValidationRoot) {
        Remove-Item -LiteralPath $portableValidationRoot -Recurse -Force
    }
}
