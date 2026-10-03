# Minecraft Instance Migration Tool v1.0.0

Initial official release of the Minecraft Instance Migration Tool.

## What's new

- **End-to-End Migration Workflow**: Seamless workflow guiding users from instance inspection to preview, backup, execution, independent verification, and comprehensive migration reporting.
- **Safety First Architecture**:
  - Independent capacity preflight check against destination volume before any write operations.
  - Fail-closed collision detection with explicit Overwrite and Skip choices.
  - Automatic backup creation before any destructive replacement.
  - Cryptographic SHA-256 independent post-write verification ensuring byte-for-byte fidelity.
  - Durable crash-resilient execution journal enabling atomic recovery diagnosis and fingerprint-guarded rollback.
- **ModPackTransfer Compatibility**:
  - 100% compatibility across 18 audited behaviors and 11 migration candidates (saves, options, keybinds, resource packs, shaders, screenshots, JourneyMap/VoxelMap waypoints, server lists, etc.).
- **Windows 11 Native Desktop UI**:
  - Accessible WPF application featuring multi-resolution icons (16x16 to 256x256).
  - Side-by-side file tree inspection and conflict visualization.
  - Safety workspace configuration and automatic privacy redaction in logs and reports.
- **Self-Contained Packaging**:
  - Standalone x64 deployment (no external .NET runtime installation required).
  - Per-user Inno Setup installer (`%LOCALAPPDATA%\Programs`) requiring lowest privileges.
  - Zero-install portable ZIP archive.
  - Cryptographic integrity verification via `SHA256SUMS.txt`.

## Safety model

1. **Preflight Validation**: Validates disk space and rejects reparse points and junction loops before touching user data.
2. **Atomic Journaling**: Records all file actions to a local durable journal before and after execution.
3. **Verified Backup**: Archives destination files prior to replace operations, revalidating backup integrity.
4. **Independent Verification**: Re-reads destination files from disk after write and verifies SHA-256 digests against source.
5. **Guarded Rollback**: Restores original files from backup if execution fails, using fingerprint guards to prevent overwriting modified data.

## Installation & Windows SmartScreen Notice

This release is distributed as an open-source community build. Because public code-signing certificates require annual enterprise subscriptions and Cloud HSM modules, the binary is not commercially signed.

- When launched on Windows, Microsoft Defender SmartScreen may display: **"Windows protected your PC"**.
- Click **"More info"** and then **"Run anyway"** to proceed.
- To verify the integrity of the downloaded executable, verify its SHA-256 hash against `SHA256SUMS.txt`:
  ```powershell
  Get-FileHash .\MinecraftInstanceMigrationTool-1.0.0-win-x64-setup.exe -Algorithm SHA256
  ```

## System Requirements

- Windows 10 (version 1809+) or Windows 11 (x64)
- 500 MB free disk space for runtime and temporary safety workspace
- Supported Minecraft launch layouts: Standard `.minecraft`, Prism Launcher, CurseForge, Modrinth App, MultiMC
