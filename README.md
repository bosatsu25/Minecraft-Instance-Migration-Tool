# Minecraft Instance Migration Tool

[日本語](README.ja.md)

Move selected Minecraft user data from an old instance to a new one when changing mod packs or launch configurations.

**v1.0.0 is complete and published:** a free, unsigned community edition for **Windows 11 x64**. Implementation, required validation, and member distribution are DONE. No required tasks remain within this release scope.

## Download and start

| Package | How to use it |
| --- | --- |
| [Installer](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/releases/download/v1.0.0/MinecraftInstanceMigrationTool-1.0.0-win-x64-setup.exe) | Run setup once, then launch the app from the Start Menu. Administrator rights are not required. |
| [Portable ZIP](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/releases/download/v1.0.0/MinecraftInstanceMigrationTool-1.0.0-win-x64.zip) | Extract the entire ZIP and run `MinecraftInstanceMigrationTool.exe`. Keep the other extracted files alongside it. |
| [SHA256SUMS.txt](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/releases/download/v1.0.0/SHA256SUMS.txt) | Check the downloaded package against its SHA-256 checksum. |

Both packages include the .NET runtime, licenses, and `START-HERE.ja.txt`. Members do not need .NET, Visual Studio, Python, or a paid service.

The app starts in Japanese. Use the top toolbar to select **English** and **Windows setting / Light / Dark** appearance. Language and theme choices last for the current session.

This edition is unsigned. Windows may show Unknown Publisher or SmartScreen warnings. Use the trusted [GitHub Release](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/releases/tag/v1.0.0) and matching checksum; a hash confirms file integrity, not publisher identity. If the source is unclear or the checksum differs, cancel. Do not disable security settings.

## Migrate an instance

1. Close Minecraft and its launcher. Keep an independent copy of important data.
2. Choose the old game folder as **Source** and the new game folder as **Destination**. Use existing, separate folders with `options.txt` or `config` directly inside them.
3. Select **Generate Preview**. Review the items without changing files.
4. Include or exclude items. For existing destination items, choose **Skip** or **Replace**, then **Apply choices**. All / None / Recommended presets apply immediately.
5. Choose a **Safety workspace** outside both game folders, then **Check capacity**.
6. Select **Execute migration**, review the confirmation, and start. Check **Migration Report** for **Completed** and successful verification before opening the new instance.

Recommended leaves `saves` and `screenshots` unselected. Include them explicitly if you want to move worlds or screenshots. Replace backs up the existing entry before replacing it; it does not merge directory contents.

Keep the safety workspace until the new instance has been checked. If migration fails or its outcome is uncertain, preserve the instance folders and recovery evidence. Use the explicitly confirmed recovery offered by the app when available.

See the [member guide](docs/member-guide.ja.txt) and [installation guide](docs/install.md) for details.

## Features and safety

- Japanese and English UI, three appearance choices, and a next-step guide.
- Read-only inspection and preview; explicit item selection and conflict decisions.
- Capacity checks, verified replacement backups, confirmation before writes, and independent post-write verification.
- Migration results and guarded recovery based on durable operation evidence.
- Files copied and verified in chunks without loading the entire file into memory.
- Local-drive paths with containment checks, including drive aliases; unsupported links and uncertain filesystem state fail closed.
- No telemetry, external services, or automatic retry/rollback.

The eleven migration candidates are:

`options.txt`, `config`, `resourcepacks`, `shaderpacks`, `schematics`, `saves`, `screenshots`, `XaeroWaypoints`, `XaeroWorldMap`, `itemscroller`, and `g4mespeed`.

The exact basename `hanemod-client.json` is excluded case-insensitively at every selected directory depth. Replace preserves an existing excluded file at the destination.

## Supported scope

v1.0.0 covers manually selected local game folders and the migration candidates above on Windows 11 x64.

Paid Authenticode signing, Windows 10 or other platforms, automatic launcher integration, Minecraft/mod/loader compatibility analysis, Merge, automatic recovery resume, report-file export, and exact NTFS cloning are **outside this release scope**. They are not pending v1.0.0 tasks. Moving data does not establish compatibility with the new mod pack.

The completed release passed 458 core tests, 10 GUI tests, packaging, install/reinstall/uninstall, and checksum verification. See the [release validation record](docs/release-validation.md), [CI](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/actions/runs/37916083840), and [package validation](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/actions/runs/37916083931).

## Development

Source builds use the SDK pinned in `global.json`:

```powershell
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build --no-restore
dotnet format --verify-no-changes --no-restore
```

Run the WPF GUI suite separately on Windows:

```powershell
dotnet test tests/MinecraftInstanceMigration.UiTests/MinecraftInstanceMigration.UiTests.csproj --configuration Release --no-restore
```

Read [AGENTS.md](AGENTS.md) before making changes. Technical details live in [architecture](docs/architecture.md), [migration rules](docs/migration-rules.md), [testing](docs/testing.md), and [release packaging](docs/release.md).

## License

[MIT](LICENSE). Runtime and dependency notices are included in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

This is an unofficial community tool, unaffiliated with Mojang Studios or Microsoft.
