# Minecraft Instance Migration Tool

[English](README.md)

MODパックや起動構成を変更するときに、旧Minecraftインスタンスから新しいインスタンスへ、選んだユーザーデータを移行するWindowsアプリです。

**v1.0.0は完成・公開済みです。** 無料の未署名コミュニティ版として、**Windows 11 x64**向けの実装・必須検証・メンバー配布まで完了しています。現行リリースのスコープに未完了タスクはありません。

## ダウンロードと起動

| 配布ファイル | 使い方 |
| --- | --- |
| [インストール版](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/releases/download/v1.0.0/MinecraftInstanceMigrationTool-1.0.0-win-x64-setup.exe) | setupを最初に実行し、導入後はスタートメニューからアプリを起動します。管理者権限は不要です。 |
| [ZIP版](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/releases/download/v1.0.0/MinecraftInstanceMigrationTool-1.0.0-win-x64.zip) | ZIP全体を展開し、中の `MinecraftInstanceMigrationTool.exe` を起動します。他の展開ファイルも一緒に保管してください。 |
| [SHA256SUMS.txt](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/releases/download/v1.0.0/SHA256SUMS.txt) | ダウンロードした配布ファイルのSHA-256を照合するためのチェックサムです。 |

どちらにも.NETランタイム、ライセンス、操作ガイド `START-HERE.ja.txt` を同梱しています。メンバーによる.NET・Visual Studio・Pythonの追加導入や、有料サービスの契約は不要です。

起動時は日本語です。画面上部で**日本語 / English**と、**Windowsに合わせる / ライト / ダーク**を切り替えられます。言語とテーマの選択は起動中のみ保持します。

未署名版のため、Windowsに「不明な発行元」やSmartScreenの警告が表示される場合があります。信頼する[GitHub Release](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/releases/tag/v1.0.0)とチェックサムを確認してください。ハッシュは配布内容の一致を確認するもので、発行元の署名ではありません。配布元が不明な場合やハッシュが一致しない場合は実行を中止してください。セキュリティ機能の無効化は不要です。

## 移行手順

1. Minecraftとランチャーを終了し、重要なデータの別コピーを保管します。
2. **移行元**に古いゲーム用フォルダー、**移行先**に新しいゲーム用フォルダーを選びます。`options.txt` や `config` が直下にある、既存の別々のフォルダーを指定してください。
3. **① 移行内容を調べる**を押し、一覧を確認します。この操作ではファイルを変更しません。
4. 移す項目を選びます。移行先に同じ項目がある場合は**スキップ / 置換**を決め、**選択を確定**します。すべて対象・すべて対象外・おすすめの操作は即時適用されます。
5. 両ゲーム用フォルダーの外側にある**バックアップ・記録の保存先**を選び、**③ 空き容量を確認**します。
6. **④ 移行を実行**で確認画面を読み、移行を開始します。**⑤ 移行結果**で「完了」と内容確認の成功を確認してから、新しいインスタンスを起動してください。

おすすめ設定では `saves` と `screenshots` は対象外です。ワールドやスクリーンショットを移す場合は対象に追加してください。置換は既存項目を先にバックアップしてから置き換える操作で、フォルダー内のデータを統合する操作ではありません。

新しいインスタンスの確認が済むまで、バックアップ・記録の保存先を保管してください。失敗や結果不明の場合は、両インスタンスと復旧記録を保持し、アプリが復旧可能と判定した場合に確認付きの復旧を使用します。

詳しくは[メンバー用ガイド](docs/member-guide.ja.txt)と[導入・操作ガイド](docs/install.md)を参照してください。

## 機能と安全性

- 日本語・英語、3種類の表示設定、次にすることを示す操作ガイド。
- 読み取り専用の調査・プレビュー、項目選択、既存項目への操作の明示的な決定。
- 空き容量の確認、置換前の検証済みバックアップ、書き込み前の最終確認、移行後の内容確認。
- 操作記録に基づく移行結果の表示と、確認付きの復旧。
- 大きなファイルも分割してコピー・検証し、ファイル全体をメモリに読み込みません。
- ローカルフォルダーの範囲確認とドライブ別名の検査。未対応のリンクや不明な状態では処理を止めます。
- テレメトリー、外部サービス、自動再実行・自動復旧はありません。

移行候補は次の11項目です。

`options.txt`、`config`、`resourcepacks`、`shaderpacks`、`schematics`、`saves`、`screenshots`、`XaeroWaypoints`、`XaeroWorldMap`、`itemscroller`、`g4mespeed`。

`hanemod-client.json` という名前のファイルは、大文字小文字を区別せず、対象フォルダー内の全階層で除外します。置換時にも、移行先にある既存の除外ファイルを保持します。

## 対応範囲と完了状況

v1.0.0の対象は、Windows 11 x64上で手動選択したローカルのゲーム用フォルダーと、上記の移行候補です。

有料のAuthenticode署名、Windows 10や他OSへの対応、ランチャーの自動連携、Minecraft・MOD・ローダーの互換性判定、既存データの統合、復旧の自動再開、結果ファイルの出力、NTFS情報の完全複製は**現行リリースのスコープ外**です。v1.0.0の未完了タスクとしては扱いません。データを移行できても、新しいMODパックとの互換性を保証するものではありません。

公開版では通常テスト458件、画面テスト10件、配布生成、導入・再導入・削除、チェックサムの検証が完了しています。[検証記録](docs/release-validation.md)、[CI](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/actions/runs/37916083840)、[配布検証](https://github.com/bosatsu25/Minecraft-Instance-Migration-Tool/actions/runs/37916083931)を参照してください。

## 開発

`global.json` で指定した.NET SDKを使用します。

```powershell
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build --no-restore
dotnet format --verify-no-changes --no-restore
```

WPFの画面テストはWindows上で別途実行します。

```powershell
dotnet test tests/MinecraftInstanceMigration.UiTests/MinecraftInstanceMigration.UiTests.csproj --configuration Release --no-restore
```

変更前に[AGENTS.md](AGENTS.md)を確認してください。技術的な詳細は[アーキテクチャ](docs/architecture.md)、[移行ルール](docs/migration-rules.md)、[テスト](docs/testing.md)、[配布手順](docs/release.md)にまとめています。

## ライセンス

[MIT](LICENSE)。ランタイムなどの通知は[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)に含まれます。

非公式のコミュニティツールです。Mojang Studios・Microsoftとの提携はありません。
