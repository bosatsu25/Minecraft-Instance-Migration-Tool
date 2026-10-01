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
$notices = Join-Path $publishDirectory "THIRD-PARTY-NOTICES.txt"

foreach ($required in @($executable, $zip, $installer, $checksums, $license, $notices)) {
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
    if (-not ($entryNames -contains "THIRD-PARTY-NOTICES.txt")) {
        throw "Portable ZIP does not contain third-party notices."
    }
    foreach ($runtimeFile in @("coreclr.dll", "hostfxr.dll", "hostpolicy.dll")) {
        if (-not ($entryNames -contains $runtimeFile)) {
            throw "Portable ZIP is missing a self-contained runtime component."
        }
    }
    if ($entryNames | Where-Object { $_ -match '(?i)(\.pdb$|\.cs$|\.xaml$|\.csproj$|\.slnx?$|\.props$|\.targets$|\.pfx$|\.p12$|\.key$|\.pem$|\.log$|\.dmp$|\.tmp$|\.user$|testhost|\.tests\.|xunit|flaui|microsoft\.net\.test|fixture|AppIcon\.(png|ico)$|(^|/)\.git(hub)?/|(^|/)(bin|obj)/)' }) {
        throw "Portable ZIP contains a forbidden source, test, fixture, or debug file."
    }
    $archiveExecutables = @($entryNames | Where-Object { $_ -match '(?i)\.exe$' })
    if (@($archiveExecutables | Where-Object {
        [System.IO.Path]::GetFileName($_) -notin @("MinecraftInstanceMigrationTool.exe", "createdump.exe")
    }).Count -ne 0) {
        throw "Portable ZIP contains an unexpected executable."
    }

    $publishedFiles = @(Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | ForEach-Object {
        [System.IO.Path]::GetRelativePath($publishDirectory, $_.FullName).Replace('\', '/')
    } | Sort-Object)
    $archivedFiles = @($entryNames | Where-Object { -not $_.EndsWith('/') } | Sort-Object)
    if ([string]::Join("`n", $publishedFiles) -ne [string]::Join("`n", $archivedFiles)) {
        throw "Portable ZIP contents do not exactly match the verified publish directory."
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
if ($expectedChecksums.Count -ne 2) {
    throw "SHA256SUMS.txt must contain exactly the ZIP and installer checksums."
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
    if (-not $RequireSigning -and $signature.Status -ne [System.Management.Automation.SignatureStatus]::NotSigned) {
        throw "Unsigned dry-run artifact did not have an unsigned Authenticode state."
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

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $root ".."))
$sourcePathTokens = @(
    $repositoryRoot,
    (Join-Path $repositoryRoot "src"),
    (Join-Path $repositoryRoot "tests")) | Select-Object -Unique
foreach ($file in Get-ChildItem -LiteralPath $publishDirectory -Recurse -File) {
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $ascii = [System.Text.Encoding]::UTF8.GetString($bytes)
    $unicode = [System.Text.Encoding]::Unicode.GetString($bytes)
    foreach ($token in $sourcePathTokens) {
        if ($ascii.Contains($token, [System.StringComparison]::OrdinalIgnoreCase) -or
            $unicode.Contains($token, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Published output contains a developer repository path."
        }
    }
}

$extractionRoot = Join-Path $env:TEMP ("mim-portable-validation-" + [Guid]::NewGuid().ToString("N"))
if (Test-Path -LiteralPath $extractionRoot) {
    throw "Portable validation refused to reuse a temporary directory."
}
try {
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $extractionRoot)
    $extractedExecutable = Join-Path $extractionRoot "MinecraftInstanceMigrationTool.exe"
    if (-not (Test-Path -LiteralPath $extractedExecutable -PathType Leaf)) {
        throw "Extracted portable package does not contain the application executable."
    }

    $process = Start-Process -FilePath $extractedExecutable -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        while (-not $process.HasExited -and $process.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 200
            $process.Refresh()
        }
        if ($process.HasExited -or $process.MainWindowHandle -eq [IntPtr]::Zero) {
            throw "Extracted portable application did not open its main window."
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
finally {
    if (Test-Path -LiteralPath $extractionRoot) {
        Remove-Item -LiteralPath $extractionRoot -Recurse -Force
    }
}

Write-Host "Verified release artifacts for $ExpectedVersion. Signed=$($RequireSigning.IsPresent)"
