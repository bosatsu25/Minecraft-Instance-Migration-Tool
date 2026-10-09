# Install and migrate

The member edition targets Windows 11 x64 and includes its own .NET runtime. Members do not need
.NET, Visual Studio, Python, Inno Setup, a licence purchase, or a signing-service subscription.
Obtain a tested package and `SHA256SUMS.txt` from your distributor. A public GitHub Release is a
separate publication step; these instructions also apply to a package shared directly with members.

## Portable ZIP (no installation)

1. Obtain `MinecraftInstanceMigrationTool-1.0.0-win-x64.zip` and the matching `SHA256SUMS.txt`.
2. Verify the hash against the checksum received from your trusted distributor.
3. Extract **the complete ZIP** into a user-owned directory, then open `MinecraftInstanceMigrationTool.exe`.
4. Read the included `START-HERE.ja.txt` for the Japanese migration steps.

Copying just the EXE is insufficient because the self-contained runtime is in the other package files.

## Installer

The alternative `MinecraftInstanceMigrationTool-1.0.0-win-x64-setup.exe` installs per-user under
`%LOCALAPPDATA%\Programs\Minecraft Instance Migration Tool`. It creates a Start Menu shortcut and
an optional desktop shortcut, and does not require administrator rights. The same member guide is
installed next to the EXE. Reinstalling the same version retains a single application registration.

## Unsigned community build

Default packages are unsigned. Windows may show Unknown Publisher or a SmartScreen warning;
the exact behaviour depends on the download channel, reputation, and machine policies. No universal
warning-free startup is promised. Do not disable security settings. If the source or checksum is
unclear, cancel and contact the distributor; managed machines may require administrator approval.

```powershell
(Get-FileHash .\MinecraftInstanceMigrationTool-1.0.0-win-x64.zip -Algorithm SHA256).Hash
Get-Content .\SHA256SUMS.txt
```

SHA-256 confirms equality with the supplied checksum, not publisher identity. It is not Authenticode.

## Migration

Close Minecraft and its launcher. In Migration Preview, choose existing source and destination game
folders (the folder with `options.txt` / `config`, not the launcher container). Generate Preview, select
items, explicitly choose Skip or Replace for conflicts, and Apply choices. Select a safety workspace
outside both game roots, Check capacity, then Execute migration and confirm. Successful completion
means Report shows Completed with successful verification. Recommended leaves saves/screenshots OFF;
choose Include or Select all if those are needed. Replace replaces the chosen entry; it does not Merge.

Keep the safety workspace and an independent copy of important data until you have checked the new
instance. A failed/uncertain run is not success: preserve files and recovery evidence and use only the
explicit guarded recovery offered by the app. There is no automatic resume or automatic rollback.

## Uninstall and support

Uninstall removes application files, shortcuts, and its registration. Minecraft roots, backups, and
journals outside the app directory are not removed. Keep migration data outside that directory.
Windows 10, automatic launcher detection, mod compatibility analysis, Merge, and report export are
not claimed. Choosing a game folder manually does not imply launcher integration.
