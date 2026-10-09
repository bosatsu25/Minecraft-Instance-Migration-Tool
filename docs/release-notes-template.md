# Minecraft Instance Migration Tool v1.0.0

日本語・英語に対応した、Windows 11 x64向けの無料コミュニティ版です。
画面上部で言語とライト・ダーク・Windows設定を切り替えられます。
「次にすること」に沿って、移行元・移行先の選択から最終確認・結果確認まで進められます。

- インストールして使う: `MinecraftInstanceMigrationTool-1.0.0-win-x64-setup.exe` を実行し、
  導入後はスタートメニューから起動します。setup.exeは導入用です。
- インストールせず使う: `MinecraftInstanceMigrationTool-1.0.0-win-x64.zip` 全体を展開し、
  中の `MinecraftInstanceMigrationTool.exe` を起動します。本体EXEだけの取り出しはできません。
- メンバーによる .NET・Visual Studio・Python の導入、有料サービスの契約は不要です。
- 未署名版のため、不明な発行元やSmartScreenの警告が出る場合があります。
  信頼する配布元と `SHA256SUMS.txt` を確認してください。セキュリティ機能の無効化は不要です。
- 詳しい操作手順は同梱 `START-HERE.ja.txt` を参照してください。

監査で見つかったバックアップ保存先のドライブ別名による内側参照を修正しました。
バックアップ前に移行元・移行先との分離を確認し、確認できない場合は書き込みを開始しません。
大きなファイルは分割してコピー・検証し、ファイル全体をメモリに読み込みません。
言語とテーマの設定は起動中のみ保持し、追加の設定保存や外部送信はありません。

A free, unofficial Windows 11 x64 tool for moving selected Minecraft user data between two
existing game folders. No affiliation with Mojang Studios or Microsoft is claimed.

## Included behaviour

- Inspect, select, Preview, capacity check, confirmed execution, independent verification, and Report.
- Eleven candidates: options.txt, config, resourcepacks, shaderpacks, schematics, saves, screenshots,
  XaeroWaypoints, XaeroWorldMap, itemscroller, g4mespeed.
- Recommended leaves saves and screenshots OFF; Include/Exclude and Select all/none are available.
- Existing destination entries require explicit Skip or Replace. Replace creates and validates backup.
- Case-insensitive exact-basename hanemod-client.json exclusion at every selected directory depth,
  including preservation of an existing destination exclusion during Replace.
- Durable execution/recovery evidence and explicitly confirmed fingerprint-guarded rollback.
  Rollback never starts automatically and an uncertain operation is not automatically resumed.

## Distribution

Choose the self-contained ZIP or per-user installer. No separate .NET installation or paid service
is needed. Both include the Japanese member guide START-HERE.ja.txt, LICENSE, and third-party notices.
Verify final packages against SHA256SUMS.txt received from the trusted distributor.

The standard community edition is unsigned; the workflow records the actual artifact signing status
above these notes. Windows may show Unknown Publisher/SmartScreen. SHA-256 confirms file integrity
against the provided checksum, not publisher identity. Do not change security settings to run the app.
If you do not trust the download, cancel and ask your distributor.

## Limits

Windows 11 x64 is the tested target. There is no Windows 10 support claim, launcher integration,
mod/version compatibility analysis, Merge, automatic recovery resume, or report export. The payload
contract does not promise exact NTFS cloning or atomic filesystem transactions. Keep important data
backed up and preserve the safety workspace until the new instance has been checked.
