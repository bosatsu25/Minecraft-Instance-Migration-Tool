[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,
    [string]$TestRoot = (Join-Path $env:TEMP ("mim-installer-validation-" + [Guid]::NewGuid().ToString("N")))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$resolvedInstaller = (Resolve-Path -LiteralPath $InstallerPath).Path
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{B8C724E4-E02C-4FDD-A3D3-4600AF40B402}_is1"
if (Test-Path -LiteralPath $uninstallKey) {
    throw "Installer validation refused to replace an existing user installation."
}
$root = [System.IO.Path]::GetFullPath($TestRoot)
$tempRoot = [System.IO.Path]::GetFullPath($env:TEMP) + [System.IO.Path]::DirectorySeparatorChar
if (-not $root.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
    [System.IO.Path]::GetFileName($root) -notmatch '^mim-installer-validation-[0-9a-f]{32}$') {
    throw "Installer validation root must be a unique temporary directory."
}

if (Test-Path -LiteralPath $root) {
    throw "Installer validation refused to use an existing temporary directory."
}
[void](New-Item -ItemType Directory -Path $root -ErrorAction Stop)

$installDirectory = Join-Path $root "app"
$installer = Start-Process -FilePath $resolvedInstaller -ArgumentList @(
    "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/DIR=$installDirectory") -Wait -PassThru
if ($installer.ExitCode -ne 0) {
    throw "Installer validation failed with exit code $($installer.ExitCode)."
}

$installedExecutable = Join-Path $installDirectory "MinecraftInstanceMigrationTool.exe"
$uninstaller = Join-Path $installDirectory "unins000.exe"
$installedLicense = Join-Path $installDirectory "LICENSE"
if (-not (Test-Path -LiteralPath $installedExecutable -PathType Leaf) -or
    -not (Test-Path -LiteralPath $uninstaller -PathType Leaf) -or
    -not (Test-Path -LiteralPath $installedLicense -PathType Leaf)) {
    throw "Installer did not create the expected application, license, and uninstaller files."
}

$uninstall = Start-Process -FilePath $uninstaller -ArgumentList @(
    "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART") -Wait -PassThru
if ($uninstall.ExitCode -ne 0) {
    throw "Uninstaller validation failed with exit code $($uninstall.ExitCode)."
}

$deadline = [DateTime]::UtcNow.AddSeconds(10)
while ((Test-Path -LiteralPath $installDirectory) -and [DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 200
}
if (Test-Path -LiteralPath $installedExecutable) {
    throw "Uninstall left the application executable behind."
}

if (((Get-Item -LiteralPath $root -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Installer validation root became a reparse point."
}
Remove-Item -LiteralPath $root -Recurse -Force
Write-Host "Per-user install and uninstall validation passed."
