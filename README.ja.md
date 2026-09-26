# Minecraft Instance Migration Tool

[English](README.md)

MOD パックや起動構成を変更する際に、旧インスタンスから新インスタンスへ
Minecraft のユーザーデータを選択的に移行する Windows デスクトップアプリを計画しています。

**現在は Phase 1 の読み取り専用 Inspector です。** 選択したローカルフォルダと
既知の直下 11 項目の属性を調査できます。コピー・バックアップ・変更は行いません。移行には使用できません。

## 目的

設定やワールドなどの個人データを守るため、移行計画を事前確認し、
バックアップと検証を伴う移行を目指します。

Inspect → Plan → Preview / Dry Run → Backup → Execute → Verify → Report

検証失敗時は Diagnose → Rollback → Report を想定しています。
Inspect より後の機能は今後実装します。

## アーキテクチャ

- **Domain:** 不変の観測モデル。移行ルールは今後実装します。
- **Application:** ユースケースとポート。Domain を参照します。
- **Infrastructure:** Windows の属性取得。Application / Domain を参照します。
- **App:** WPF の View、MVVM ViewModel、依存関係の組み立て。
- **Tests:** xUnit による動作・統合・ViewModel テストと、責務境界の回帰検出。

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

## Inspector の使い方

**Browse** で任意の名前のローカルフォルダを選び、**Inspect** を押します。
ローカルドライブの絶対パスを入力することもできます。**Cancel** は属性取得の合間に適用されます。
入力変更時には以前の結果を消去します。対象は直下の次の 11 項目だけです。
`options.txt`、`config`、`resourcepacks`、`shaderpacks`、`schematics`、`saves`、
`screenshots`、`XaeroWaypoints`、`XaeroWorldMap`、`itemscroller`、`g4mespeed`。

期待する種類と実際の状態を別々に表示します。欠落とアクセス拒否・取得失敗は区別します。
リンクは追跡せず、ルートまたはその祖先がリンクなら調査を停止します。
再帰走査や内容の読み取りは行いません。既知名の存在は Minecraft インスタンス・互換性・
移行可否の証明ではありません。結果は複数時点の観測です。
UNC・ネットワークドライブ・デバイスパス・相対パス・親への遡上は未対応です。

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

少数の WPF UI スモークテストは Windows 上で別実行します。

```powershell
dotnet test tests/MinecraftInstanceMigration.UiTests/MinecraftInstanceMigration.UiTests.csproj --configuration Release
```

対象範囲と手動スモーク手順は [testing](docs/testing.md) に記載しています。

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
