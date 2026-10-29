# Minecraft Instance Migration Tool v1.1.0

日本語・英語に対応した、Windows 11 x64向けの無料コミュニティ版です。
移行先に同じ項目があるときの選択肢に「統合」を追加しました。v1.0.1の安全性の修正もすべて含みます。
インストール版は、新しい setup.exe を実行すると同じ場所に上書きで更新されます。

## v1.1.0の変更点

- 「スキップ」「置換」に加えて「統合」を選べるようになりました。
  統合は、移行先にだけあるファイルを残し、同じ名前のファイルを移行元の内容で上書きします。
  新しいMODパックが追加した設定を残したまま、自分の設定を `config` へ移せます。
- `resourcepacks`・`shaderpacks`・Xaero のフォルダーは、パックや保存先の単位で入れ替えます。
  `options.txt` と `saves` には統合はありません（ワールドをファイル単位で混ぜると壊れるためです）。
- 「選択を確定」すると統合する内容を比べ、追加・上書き・保持の件数を表示します。この比較ではファイルを変更しません。
- 統合も置換と同じく、実行前に移行先の項目をバックアップし、実行後に内容を確認し、確認付きの復旧で元に戻せます。

## 使い方

- インストールして使う: `MinecraftInstanceMigrationTool-1.1.0-win-x64-setup.exe` を実行し、
  導入後はスタートメニューから起動します。setup.exeは導入用です。
- インストールせず使う: `MinecraftInstanceMigrationTool-1.1.0-win-x64.zip` 全体を展開し、
  中の `MinecraftInstanceMigrationTool.exe` を起動します。本体EXEだけの取り出しはできません。
- メンバーによる .NET・Visual Studio・Python の導入、有料サービスの契約は不要です。
- 未署名版のため、不明な発行元やSmartScreenの警告が出る場合があります。
  実行する前に、ダウンロードしたファイルのSHA-256を `SHA256SUMS.txt` と照合してください。
  セキュリティ機能の無効化は不要です。
- 詳しい操作手順は同梱 `START-HERE.ja.txt` を参照してください。

言語とテーマの設定は起動中のみ保持し、追加の設定保存や外部送信はありません。

A free, unofficial Windows 11 x64 tool for moving selected Minecraft user data between two
existing game folders. No affiliation with Mojang Studios or Microsoft is claimed.

## Changes in v1.1.0

- Existing destination directories gain a third explicit choice, Merge, next to Skip and Replace. Merge keeps
  files that exist only in the destination and overwrites the rest with the source, so a new mod pack's added
  configuration survives while the member's own settings move over.
- `resourcepacks`, `shaderpacks`, and the Xaero folders merge per pack or per world/server folder. Merge is not
  offered for `options.txt` or `saves`, because a file-level mix of two worlds corrupts them.
- Applying choices compares both folders read-only and shows the added, overwritten, and kept counts.
- Like Replace, Merge backs up the destination entry first, verifies the result, and supports confirmed,
  fingerprint-guarded rollback.
- Includes every v1.0.1 safety fix.

## Included behaviour

- Inspect, select, Preview, capacity check, confirmed execution, independent verification, and Report.
- Eleven candidates: options.txt, config, resourcepacks, shaderpacks, schematics, saves, screenshots,
  XaeroWaypoints, XaeroWorldMap, itemscroller, g4mespeed.
- Recommended leaves saves and screenshots OFF; Include/Exclude and Select all/none are available.
- Existing destination entries require explicit Skip, Merge, or Replace. Merge and Replace create and validate a backup first.
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
mod/version compatibility analysis, Merge of worlds, automatic recovery resume, or report export. The payload
contract does not promise exact NTFS cloning or atomic filesystem transactions. Keep important data
backed up and preserve the safety workspace until the new instance has been checked.
