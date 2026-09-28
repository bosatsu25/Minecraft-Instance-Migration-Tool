# Install and upgrade

No stable download exists yet. The release pipeline currently produces unsigned Phase 5.0 verification artifacts; the first supported public artifact is pending Phase 5.1.

## Installer

1. Download the `win-x64-setup.exe` and `SHA256SUMS.txt` assets from the same release.
2. Verify the SHA-256 value.
3. Confirm the Authenticode publisher for a production release.
4. Run the installer and launch the app from the Start Menu.

The installer is per-user and uses `%LOCALAPPDATA%\Programs\Minecraft Instance Migration Tool`, so it does not request administrator rights. A desktop shortcut is optional. The stable application identity lets a newer compatible installer update the existing installation while preserving shortcuts and uninstall registration.

## Portable

1. Download the `win-x64.zip` and `SHA256SUMS.txt` assets.
2. Verify the SHA-256 value.
3. Extract the complete ZIP to a user-owned directory.
4. Run `MinecraftInstanceMigrationTool.exe`.

Example checksum verification:

```powershell
(Get-FileHash .\MinecraftInstanceMigrationTool-0.9.0-win-x64.zip -Algorithm SHA256).Hash
Get-Content .\SHA256SUMS.txt
```

Compare the values exactly. The portable package is self-contained and does not require a separate .NET installation.

## Upgrade and uninstall

Run the newer installer over the existing per-user installation. Downgrades are not supported for v1.0. Uninstall removes installed application files and shortcuts only. Migration sources, destinations, safety workspaces, journals, backups, and user-selected data live outside the installer-owned directory and are not deleted.

Supported release environment: Windows 11 x64. Windows 10 has not been validated and is not currently claimed as supported.
