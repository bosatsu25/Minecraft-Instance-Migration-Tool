[CmdletBinding()]
param(
    [string]$DotNetPath = "dotnet",
    [Parameter(Mandatory)]
    [string]$IsccPath,
    [string]$SignToolPath,
    [string]$SigningCertificatePath,
    [string]$TimestampUrl,
    [string]$ExpectedVersion,
    [switch]$RequireSigning,
    [switch]$SkipLaunchSmoke
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$projectPath = Join-Path $repositoryRoot "src\MinecraftInstanceMigration.App\MinecraftInstanceMigration.App.csproj"
$profilePath = Join-Path $repositoryRoot "src\MinecraftInstanceMigration.App\Properties\PublishProfiles\WinX64.pubxml"
$installerScript = Join-Path $repositoryRoot "eng\release\installer.iss"
$artifactsRoot = Join-Path $repositoryRoot "artifacts"
$publishDirectory = Join-Path $artifactsRoot "publish\win-x64"
$packageDirectory = Join-Path $artifactsRoot "release"
$propsPath = Join-Path $repositoryRoot "Directory.Build.props"

function Assert-OwnedOutputPath([string]$Path) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $ownedRoot = [System.IO.Path]::GetFullPath($artifactsRoot) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($ownedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify an output path outside the repository artifacts directory."
    }
}

function Invoke-Checked([string]$FilePath, [string[]]$Arguments) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "A release tool exited with code $LASTEXITCODE."
    }
}

function Get-ReleaseVersion {
    [xml]$props = Get-Content -LiteralPath $propsPath -Raw
    $nodes = @($props.SelectNodes('/Project/PropertyGroup/Version'))
    if ($nodes.Count -ne 1) {
        throw "Directory.Build.props must contain exactly one Version value."
    }

    $value = [string]$nodes[0].InnerText
    if ($value -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
        throw "The central Version is not a supported semantic version."
    }

    return $value
}

function Invoke-AuthenticodeSigning([string]$Path) {
    if (-not $RequireSigning) {
        return
    }

    $password = $env:MIM_SIGNING_CERTIFICATE_PASSWORD
    if ([string]::IsNullOrWhiteSpace($SignToolPath) -or
        [string]::IsNullOrWhiteSpace($SigningCertificatePath) -or
        [string]::IsNullOrWhiteSpace($TimestampUrl) -or
        [string]::IsNullOrWhiteSpace($password)) {
        throw "Trusted release signing was requested, but signing configuration is incomplete."
    }

    Invoke-Checked $SignToolPath @(
        "sign", "/fd", "SHA256", "/td", "SHA256", "/tr", $TimestampUrl,
        "/f", $SigningCertificatePath, "/p", $password, $Path)

    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Authenticode verification failed for a signed release artifact."
    }
}

function Assert-SignatureState([string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($RequireSigning) {
        if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
            throw "Expected a valid Authenticode signature."
        }
    }
    elseif ($signature.Status -eq [System.Management.Automation.SignatureStatus]::Valid) {
        throw "Unsigned dry-run unexpectedly produced a signed artifact."
    }
}

function Test-PublishedApplication([string]$ExecutablePath) {
    if ($SkipLaunchSmoke) {
        return
    }

    $process = Start-Process -FilePath $ExecutablePath -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        while (-not $process.HasExited -and $process.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 200
            $process.Refresh()
        }

        if ($process.HasExited) {
            throw "Published application exited before opening its main window."
        }
        if ($process.MainWindowHandle -eq [IntPtr]::Zero) {
            throw "Published application did not open its main window."
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

$version = Get-ReleaseVersion
if ($ExpectedVersion -and $ExpectedVersion -ne $version) {
    throw "Expected version '$ExpectedVersion' does not match central version '$version'."
}

if (-not (Test-Path -LiteralPath $DotNetPath -PathType Leaf) -and -not (Get-Command $DotNetPath -ErrorAction SilentlyContinue)) {
    throw "The configured dotnet executable was not found."
}
if (-not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw "The configured Inno Setup compiler was not found."
}

Assert-OwnedOutputPath $publishDirectory
Assert-OwnedOutputPath $packageDirectory
foreach ($directory in @($publishDirectory, $packageDirectory)) {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
    [void](New-Item -ItemType Directory -Path $directory)
}

Invoke-Checked $DotNetPath @("restore", $projectPath, "--runtime", "win-x64")
Invoke-Checked $DotNetPath @(
    "publish", $projectPath,
    "--configuration", "Release",
    "--runtime", "win-x64",
    "--self-contained", "true",
    "--no-restore",
    "-p:PublishProfile=$profilePath",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "--output", $publishDirectory)

Copy-Item -LiteralPath (Join-Path $repositoryRoot "THIRD-PARTY-NOTICES.txt") -Destination $publishDirectory

$executable = Join-Path $publishDirectory "MinecraftInstanceMigrationTool.exe"
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "The expected published executable was not produced."
}

$forbidden = Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | Where-Object {
    $_.Extension -in @(".pdb", ".cs", ".xaml") -or
    $_.Name -match '(?i)(testhost|\.tests\.|xunit|flaui|fixture)'
}
if ($forbidden) {
    throw "The publish directory contains test, source, fixture, or debug files."
}

$versionInfo = (Get-Item -LiteralPath $executable).VersionInfo
if ($versionInfo.ProductName -ne "Minecraft Instance Migration Tool" -or
    $versionInfo.FileDescription -ne "Minecraft Instance Migration Tool" -or
    $versionInfo.CompanyName -ne "bosatsuKing") {
    throw "Published Windows product metadata is incomplete or inconsistent."
}
if ($versionInfo.ProductVersion -ne $version) {
    throw "Published ProductVersion does not match the central version."
}
if ($versionInfo.FileVersion -ne "$version.0") {
    throw "Published FileVersion does not match the central version."
}

Invoke-AuthenticodeSigning $executable
Assert-SignatureState $executable
Test-PublishedApplication $executable

$zipName = "MinecraftInstanceMigrationTool-$version-win-x64.zip"
$zipPath = Join-Path $packageDirectory $zipName
Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $zipPath -CompressionLevel Optimal

$fileVersion = "$version.0"
Invoke-Checked $IsccPath @(
    "/Qp",
    "/DAppVersion=$version",
    "/DFileVersion=$fileVersion",
    "/DPublishDir=$publishDirectory",
    "/DPackageDir=$packageDirectory",
    $installerScript)

$installerName = "MinecraftInstanceMigrationTool-$version-win-x64-setup.exe"
$installerPath = Join-Path $packageDirectory $installerName
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "The expected installer was not produced."
}

Invoke-AuthenticodeSigning $installerPath
Assert-SignatureState $installerPath

$checksumPath = Join-Path $packageDirectory "SHA256SUMS.txt"
$checksumLines = foreach ($artifact in @($zipPath, $installerPath)) {
    $hash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash *$([System.IO.Path]::GetFileName($artifact))"
}
[System.IO.File]::WriteAllLines($checksumPath, $checksumLines, [System.Text.UTF8Encoding]::new($false))

& (Join-Path $PSScriptRoot "Verify-Release.ps1") -ArtifactsRoot $artifactsRoot -ExpectedVersion $version -RequireSigning:$RequireSigning

Write-Host "Release package ready: version=$version signing=$($RequireSigning.IsPresent)"
