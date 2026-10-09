[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,
    [Parameter(Mandatory)]
    [string]$ExpectedVersion,
    [string]$PreviousInstallerPath,
    [string]$PreviousVersion,
    [switch]$TestReinstall,
    [switch]$CreateDesktopShortcut,
    [switch]$RequireSigning,
    [switch]$UseDefaultInstallPath,
    [string]$TestRoot = (Join-Path $env:TEMP ("mim-installer-validation-" + [Guid]::NewGuid().ToString("N")))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$resolvedInstaller = (Resolve-Path -LiteralPath $InstallerPath).Path
$resolvedPreviousInstaller = $null
if (-not [string]::IsNullOrWhiteSpace($PreviousInstallerPath)) {
    if ([string]::IsNullOrWhiteSpace($PreviousVersion)) {
        throw "PreviousVersion is required when PreviousInstallerPath is provided."
    }
    $resolvedPreviousInstaller = (Resolve-Path -LiteralPath $PreviousInstallerPath).Path
}
elseif (-not [string]::IsNullOrWhiteSpace($PreviousVersion)) {
    throw "PreviousInstallerPath is required when PreviousVersion is provided."
}
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{B8C724E4-E02C-4FDD-A3D3-4600AF40B402}_is1"
$machineUninstallKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{B8C724E4-E02C-4FDD-A3D3-4600AF40B402}_is1"
$startMenuShortcut = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) `
    "Minecraft Instance Migration Tool\Minecraft Instance Migration Tool.lnk"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)) `
    "Minecraft Instance Migration Tool.lnk"
if (Test-Path -LiteralPath $uninstallKey) {
    throw "Installer validation refused to replace an existing user installation."
}
if ((Test-Path -LiteralPath $machineUninstallKey) -or
    (Test-Path -LiteralPath $startMenuShortcut) -or
    (Test-Path -LiteralPath $desktopShortcut)) {
    throw "Installer validation refused to reuse existing machine registration or shortcuts."
}
if (Get-Process -Name "MinecraftInstanceMigrationTool" -ErrorAction SilentlyContinue) {
    throw "Installer validation requires that no application process is already running."
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
$installDirectory = if ($UseDefaultInstallPath) {
    Join-Path $env:LOCALAPPDATA "Programs\Minecraft Instance Migration Tool"
}
else {
    Join-Path $root "app"
}
if (Test-Path -LiteralPath $installDirectory) {
    throw "Installer validation refused to reuse an existing installation directory."
}
[void](New-Item -ItemType Directory -Path $root -ErrorAction Stop)

$userDataDirectory = Join-Path $root "user-data"
[void](New-Item -ItemType Directory -Path $userDataDirectory)
$userDataSentinel = Join-Path $userDataDirectory "must-survive-uninstall.txt"
[System.IO.File]::WriteAllText($userDataSentinel, "owned migration data")

function Invoke-Installer([string]$PackagePath, [switch]$WithDesktopShortcut) {
    $arguments = @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART")
    if (-not $UseDefaultInstallPath) {
        $arguments += "/DIR=$installDirectory"
    }
    if ($WithDesktopShortcut) {
        $arguments += "/TASKS=desktopicon"
    }
    else {
        $arguments += "/MERGETASKS=!desktopicon"
    }

    $installer = Start-Process -FilePath $PackagePath -ArgumentList $arguments -Wait -PassThru
    if ($installer.ExitCode -ne 0) {
        throw "Installer validation failed with exit code $($installer.ExitCode)."
    }
}

function Test-ApplicationLaunch([string]$LaunchPath, [string]$Surface) {
    Start-Process -FilePath $LaunchPath
    $process = $null
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        while ($null -eq $process -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 200
            $process = Get-Process -Name "MinecraftInstanceMigrationTool" -ErrorAction SilentlyContinue |
                Select-Object -First 1
        }
        if ($null -eq $process) {
            throw "$Surface did not start the installed application."
        }

        while (-not $process.HasExited -and $process.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 200
            $process.Refresh()
        }
        if ($process.HasExited -or $process.MainWindowHandle -eq [IntPtr]::Zero) {
            throw "$Surface did not open the application window."
        }
    }
    finally {
        if ($null -ne $process -and -not $process.HasExited) {
            [void]$process.CloseMainWindow()
            if (-not $process.WaitForExit(5000)) {
                Stop-Process -Id $process.Id -Force
                $process.WaitForExit()
            }
        }
        if ($null -ne $process) {
            $process.Dispose()
        }
    }
}

$validationSucceeded = $false
try {
if ($null -ne $resolvedPreviousInstaller) {
    Invoke-Installer $resolvedPreviousInstaller
    $previousExecutable = Join-Path $installDirectory "MinecraftInstanceMigrationTool.exe"
    if (-not (Test-Path -LiteralPath $previousExecutable -PathType Leaf) -or
        (Get-Item -LiteralPath $previousExecutable).VersionInfo.ProductVersion -ne $PreviousVersion) {
        throw "Previous-version installer did not produce the expected application version."
    }
    Test-ApplicationLaunch $previousExecutable "Previous-version direct executable launch"
}

Invoke-Installer $resolvedInstaller

$installedExecutable = Join-Path $installDirectory "MinecraftInstanceMigrationTool.exe"
$uninstaller = Join-Path $installDirectory "unins000.exe"
$installedLicense = Join-Path $installDirectory "LICENSE"
$installedNotices = Join-Path $installDirectory "THIRD-PARTY-NOTICES.txt"
$installedMemberGuide = Join-Path $installDirectory "START-HERE.ja.txt"
if (-not (Test-Path -LiteralPath $installedExecutable -PathType Leaf) -or
    -not (Test-Path -LiteralPath $uninstaller -PathType Leaf) -or
    -not (Test-Path -LiteralPath $installedLicense -PathType Leaf) -or
    -not (Test-Path -LiteralPath $installedNotices -PathType Leaf) -or
    -not (Test-Path -LiteralPath $installedMemberGuide -PathType Leaf) -or
    -not (Test-Path -LiteralPath $startMenuShortcut -PathType Leaf)) {
    throw "Installer did not create the expected application, notices, license, uninstaller, and Start Menu shortcut."
}
if (Test-Path -LiteralPath $desktopShortcut) {
    throw "Installer created the optional desktop shortcut when it was not selected."
}

$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($startMenuShortcut)
$uninstallEntry = Get-ItemProperty -LiteralPath $uninstallKey
$installedVersion = (Get-Item -LiteralPath $installedExecutable).VersionInfo
if (-not [string]::Equals($shortcut.TargetPath, $installedExecutable, [StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals($shortcut.IconLocation, "$installedExecutable,0", [StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals([string]$uninstallEntry.DisplayIcon, $installedExecutable, [StringComparison]::OrdinalIgnoreCase) -or
    $uninstallEntry.DisplayName -ne "Minecraft Instance Migration Tool" -or
    $uninstallEntry.Publisher -ne "bosatsuKing" -or
    $uninstallEntry.DisplayVersion -ne $ExpectedVersion -or
    $installedVersion.ProductVersion -ne $ExpectedVersion -or
    $installedVersion.FileVersion -ne "$ExpectedVersion.0" -or
    -not [string]::Equals([string]$uninstallEntry.InstallLocation.TrimEnd('\'), $installDirectory, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Installed application, shortcut, or uninstall metadata is inconsistent."
}
if (Test-Path -LiteralPath $machineUninstallKey) {
    throw "Per-user installation unexpectedly created a machine-wide uninstall registration."
}
$installedSignature = Get-AuthenticodeSignature -LiteralPath $installedExecutable
if ($RequireSigning -and $installedSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "Installed application failed required Authenticode verification."
}
if (-not $RequireSigning -and $installedSignature.Status -eq [System.Management.Automation.SignatureStatus]::Valid) {
    throw "Unsigned validation unexpectedly installed a signed application."
}

Test-ApplicationLaunch $installedExecutable "Direct executable launch"
Test-ApplicationLaunch $startMenuShortcut "Start Menu shortcut"

if ($TestReinstall) {
    Invoke-Installer $resolvedInstaller -WithDesktopShortcut:$CreateDesktopShortcut
    if (-not (Test-Path -LiteralPath $installedExecutable -PathType Leaf) -or
        -not (Test-Path -LiteralPath $uninstallKey)) {
        throw "Same-version reinstall corrupted the installation."
    }
    if (@(Get-ChildItem -LiteralPath (Split-Path $startMenuShortcut) -Filter "Minecraft Instance Migration Tool.lnk").Count -ne 1) {
        throw "Same-version reinstall created duplicate Start Menu shortcuts."
    }
    if ($CreateDesktopShortcut) {
        if (-not (Test-Path -LiteralPath $desktopShortcut -PathType Leaf)) {
            throw "Reinstall did not create the selected desktop shortcut."
        }
        $desktop = (New-Object -ComObject WScript.Shell).CreateShortcut($desktopShortcut)
        if (-not [string]::Equals($desktop.TargetPath, $installedExecutable, [StringComparison]::OrdinalIgnoreCase) -or
            -not [string]::Equals($desktop.IconLocation, "$installedExecutable,0", [StringComparison]::OrdinalIgnoreCase)) {
            throw "Desktop shortcut target or icon is incorrect."
        }
        Test-ApplicationLaunch $desktopShortcut "Desktop shortcut"
    }
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
if ((Test-Path -LiteralPath $startMenuShortcut) -or (Test-Path -LiteralPath $uninstallKey)) {
    throw "Uninstall left its shortcut or registration behind."
}
if (Test-Path -LiteralPath $desktopShortcut) {
    throw "Uninstall left the desktop shortcut behind."
}
if (-not (Test-Path -LiteralPath $userDataSentinel -PathType Leaf) -or
    [System.IO.File]::ReadAllText($userDataSentinel) -ne "owned migration data") {
    throw "Uninstall removed or changed user-owned migration data outside the installation directory."
}

if (((Get-Item -LiteralPath $root -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Installer validation root became a reparse point."
}
Remove-Item -LiteralPath $root -Recurse -Force
$validationSucceeded = $true
}
finally {
    if (-not $validationSucceeded) {
        $candidateUninstaller = Join-Path $installDirectory "unins000.exe"
        if (Test-Path -LiteralPath $candidateUninstaller -PathType Leaf) {
            try {
                $cleanup = Start-Process -FilePath $candidateUninstaller -ArgumentList @(
                    "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART") -Wait -PassThru
                if ($cleanup.ExitCode -ne 0) {
                    Write-Warning "Failed validation cleanup uninstaller exited with code $($cleanup.ExitCode)."
                }
            }
            catch {
                Write-Warning "Failed validation cleanup could not run the owned uninstaller."
            }
        }

        foreach ($candidateShortcut in @($startMenuShortcut, $desktopShortcut)) {
            if (Test-Path -LiteralPath $candidateShortcut -PathType Leaf) {
                try {
                    $candidate = (New-Object -ComObject WScript.Shell).CreateShortcut($candidateShortcut)
                    if ([System.IO.Path]::GetFullPath($candidate.TargetPath).StartsWith(
                        [System.IO.Path]::GetFullPath($installDirectory) + [System.IO.Path]::DirectorySeparatorChar,
                        [System.StringComparison]::OrdinalIgnoreCase)) {
                        Remove-Item -LiteralPath $candidateShortcut -Force
                    }
                }
                catch {
                    Write-Warning "Failed validation cleanup could not inspect an owned shortcut."
                }
            }
        }

        if (Test-Path -LiteralPath $uninstallKey) {
            $candidateEntry = Get-ItemProperty -LiteralPath $uninstallKey
            if ([string]::Equals(
                ([string]$candidateEntry.InstallLocation).TrimEnd('\'),
                $installDirectory,
                [System.StringComparison]::OrdinalIgnoreCase)) {
                Remove-Item -LiteralPath $uninstallKey -Recurse -Force
            }
        }
        if (Test-Path -LiteralPath $root) {
            Remove-Item -LiteralPath $root -Recurse -Force
        }
    }
}
Write-Host "Per-user install, launch, reinstall, shortcut, and uninstall validation passed."
