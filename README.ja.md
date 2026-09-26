# Minecraft Instance Migration Tool

[English](README.md)

MOD パックや起動構成を変更する際に、旧インスタンスから新インスタンスへ
Minecraft のユーザーデータを選択的に移行する Windows デスクトップアプリを計画しています。

**現在は Phase 0 の開発基盤です。** アプリは静的な WPF shell のみで、
Minecraft ファイルの調査・コピー・バックアップ・変更は行いません。実際の移行には使用できません。

## 目的

設定やワールドなどの個人データを守るため、移行計画を事前確認し、
バックアップと検証を伴う移行を目指します。

Inspect → Plan → Preview / Dry Run → Backup → Execute → Verify → Report

検証失敗時は Diagnose → Rollback → Report を想定しています。
これらの機能は今後実装し、Phase 0 では責務境界と検証手段を整備します。

## アーキテクチャ

- **Domain:** 純粋なルールと値モデル。まだ実装しません。
- **Application:** ユースケースとポート。Domain を参照します。
- **Infrastructure:** 将来のファイルシステム実装。Application / Domain を参照します。
- **App:** WPF の View、将来の MVVM ViewModel、依存関係の組み立て。
- **Tests:** UI 以外の各層に xUnit プロジェクトを設け、境界の回帰を検出します。

App は組み立てのため Application / Infrastructure を参照します。
Application / Domain から Infrastructure を参照しません。
詳しくは [architecture](docs/architecture.md) を参照してください。

## 開発

Windows と [global.json](global.json) 指定の .NET 10 SDK
（10.0.401、同じ feature band の最新パッチを許容）が必要です。
WPF ビルドツールは SDK に含まれます。IDE は必須ではありません。

リポジトリのルートで実行します。

```powershell
dotnet restore
dotnet build --configuration Release --no-restore
dotnet run --project src/MinecraftInstanceMigration.App --configuration Release --no-build
```

依存関係の restore には NuGet への接続が必要です。アプリ自体に通信機能はありません。
ライブラリは `net10.0`、WPF は `net10.0-windows` を使用します。
Windows や SDK がないことを理由にターゲットを変更しないでください。

## 検証

```powershell
dotnet test --configuration Release --no-build --no-restore
dotnet format --verify-no-changes --no-restore
git diff --check
git status --short --branch
```

先に restore / build を実行します。[Windows CI](.github/workflows/ci.yml) も
push / pull request 時に restore・Release build・test・format 検証を行います。
警告はビルド失敗として扱います。C# の整形修正には `dotnet format --no-restore` を使用します。
Markdown・YAML・XAML のレイアウトすべてを formatter が検証するわけではないため、差分も確認します。

開発は [Evidence-driven Graph Loop](docs/graph-loop.md) に従います。
恒久ルールは [AGENTS.md](AGENTS.md)、検証範囲は [testing](docs/testing.md) を参照してください。

## ロードマップ

1. Phase 0: solution、責務境界、テスト、CI、ドキュメント、最小 shell。
2. Phase 1: 読み取り専用の Instance Inspector。
3. 以後は Planner → Rules → Preview → Backup → Executor → Verifier → Report、
   および失敗時の診断・ロールバックを個別に実装。

ノード単位で Issue / PR を分け、受け入れテストとともに進めます。
旧版由来の移行候補は [migration rules](docs/migration-rules.md) に整理しています。
これらは未実装で、互換性の保証ではありません。
