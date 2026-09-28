[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ArtifactsRoot,
    [Parameter(Mandatory)]
    [string]$ExpectedVersion,
    [switch]$RequireSigning
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = [System.IO.Path]::GetFullPath($ArtifactsRoot)
$publishDirectory = Join-Path $root "publish\win-x64"
$packageDirectory = Join-Path $root "release"
$executable = Join-Path $publishDirectory "MinecraftInstanceMigrationTool.exe"
$zip = Join-Path $packageDirectory "MinecraftInstanceMigrationTool-$ExpectedVersion-win-x64.zip"
$installer = Join-Path $packageDirectory "MinecraftInstanceMigrationTool-$ExpectedVersion-win-x64-setup.exe"
$checksums = Join-Path $packageDirectory "SHA256SUMS.txt"
$license = Join-Path $publishDirectory "LICENSE"

foreach ($required in @($executable, $zip, $installer, $checksums, $license)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required release artifact is missing: $([System.IO.Path]::GetFileName($required))"
    }
}

$unexpectedPackages = @(Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object {
    $_.Name -notin @(
        "MinecraftInstanceMigrationTool-$ExpectedVersion-win-x64.zip",
        "MinecraftInstanceMigrationTool-$ExpectedVersion-win-x64-setup.exe",
        "SHA256SUMS.txt")
})
if ($unexpectedPackages.Count -ne 0) {
    throw "The release directory contains unexpected files."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $entryNames = @($archive.Entries | ForEach-Object FullName)
    if (-not ($entryNames -contains "MinecraftInstanceMigrationTool.exe")) {
        throw "Portable ZIP does not contain the application executable."
    }
    if (-not ($entryNames -contains "LICENSE")) {
        throw "Portable ZIP does not contain the repository license."
    }
    if ($entryNames | Where-Object { $_ -match '(?i)(\.pdb$|\.cs$|\.xaml$|testhost|\.tests\.|xunit|flaui|fixture|AppIcon\.(png|ico)$)' }) {
        throw "Portable ZIP contains a forbidden source, test, fixture, or debug file."
    }
}
finally {
    $archive.Dispose()
}

$expectedChecksums = @{}
foreach ($line in Get-Content -LiteralPath $checksums) {
    if ($line -notmatch '^([0-9a-f]{64}) \*(.+)$') {
        throw "SHA256SUMS.txt contains an invalid line."
    }
    $expectedChecksums[$Matches[2]] = $Matches[1]
}
foreach ($path in @($zip, $installer)) {
    $name = [System.IO.Path]::GetFileName($path)
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expectedChecksums[$name] -ne $actual) {
        throw "SHA-256 verification failed for $name."
    }
}

foreach ($path in @($executable, $installer)) {
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($RequireSigning -and $signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Signed release artifact failed Authenticode verification."
    }
    if (-not $RequireSigning -and $signature.Status -eq [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Unsigned dry-run artifact was mislabeled by its actual signature state."
    }
}

$applicationVersion = (Get-Item -LiteralPath $executable).VersionInfo
if ($applicationVersion.ProductVersion -ne $ExpectedVersion -or
    $applicationVersion.FileVersion -ne "$ExpectedVersion.0") {
    throw "Application version metadata does not match the expected release version."
}
$installerVersion = (Get-Item -LiteralPath $installer).VersionInfo
if ($installerVersion.ProductVersion.Trim() -ne $ExpectedVersion -or
    $installerVersion.FileVersion.Trim() -ne "$ExpectedVersion.0") {
    throw "Installer version metadata does not match the expected release version."
}

Write-Host "Verified release artifacts for $ExpectedVersion. Signed=$($RequireSigning.IsPresent)"
