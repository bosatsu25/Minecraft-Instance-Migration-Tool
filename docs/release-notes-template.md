# Minecraft Instance Migration Tool v1.0.1

日本語・英語に対応した、Windows 11 x64向けの無料コミュニティ版です。
v1.0.0の置換を安全にする更新です。v1.0.0を使っている場合は、このバージョンに更新してください。
インストール版は、新しい setup.exe を実行すると同じ場所に上書きで更新されます。

## v1.0.1の変更点

- 置換の直前に、移行先がバックアップを取ったときと同じ内容か確認するようにしました。
  バックアップの後に移行先が変わっていた場合は、何も書き換えずに止まります。
  その場合は内容を確認してから、「① 移行内容を調べる」からやり直してください。
  （v1.0.0では、バックアップの後に移行先へ加わった変更が、置換で消える可能性がありました。）
- 移行先の項目が想定と違う種類の場合（例: `config` がフォルダーではなくファイル）、
  置換は移行前の確認の段階で止まるようになりました。その項目はスキップを選べます。
  （v1.0.0では実行を始めた後に止まり、「復旧が必要」と表示されていました。データは変更されません。）
- 移行できない理由を、日本語表示ではすべて日本語で表示するようにしました。
- 大きな `saves` などの検証を速くしました。

## 使い方

- インストールして使う: `MinecraftInstanceMigrationTool-1.0.1-win-x64-setup.exe` を実行し、
  導入後はスタートメニューから起動します。setup.exeは導入用です。
- インストールせず使う: `MinecraftInstanceMigrationTool-1.0.1-win-x64.zip` 全体を展開し、
  中の `MinecraftInstanceMigrationTool.exe` を起動します。本体EXEだけの取り出しはできません。
- メンバーによる .NET・Visual Studio・Python の導入、有料サービスの契約は不要です。
- 未署名版のため、不明な発行元やSmartScreenの警告が出る場合があります。
  実行する前に、ダウンロードしたファイルのSHA-256を `SHA256SUMS.txt` と照合してください。
  セキュリティ機能の無効化は不要です。
- 詳しい操作手順は同梱 `START-HERE.ja.txt` を参照してください。

言語とテーマの設定は起動中のみ保持し、追加の設定保存や外部送信はありません。

A free, unofficial Windows 11 x64 tool for moving selected Minecraft user data between two
existing game folders. No affiliation with Mojang Studios or Microsoft is claimed.

## Changes in v1.0.1

- Replace now proves, immediately before it starts, that the destination still equals what the backup
  preserved. If the destination changed after Backup, the run stops without writing anything. In v1.0.0
  such a change could be deleted by Replace without a copy.
- A Replace whose destination has a different kind than expected (for example `config` as a file) is
  refused during planning, where Skip remains available, instead of stopping after execution started.
- Every blocked reason is translated in the Japanese UI.
- Verification of large folders such as `saves` is faster.
- For distributors: tag releases from v1.0.1 on carry a GitHub build-provenance attestation for the ZIP
  and installer, which `gh attestation verify` can check (see docs/install.md).

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
Check the SHA-256 of the downloaded package against SHA256SUMS.txt before running it.

The standard community edition is unsigned; the workflow records the actual artifact signing status
above these notes. Windows may show Unknown Publisher/SmartScreen. SHA-256 confirms that a file equals
the published one, not who built it. Do not change security settings to run the app.
If you do not trust the download, cancel and ask your distributor.

## Limits

Windows 11 x64 is the tested target. There is no Windows 10 support claim, launcher integration,
mod/version compatibility analysis, Merge, automatic recovery resume, or report export. The payload
contract does not promise exact NTFS cloning or atomic filesystem transactions. Keep important data
backed up and preserve the safety workspace until the new instance has been checked.
