# Minecraft Instance Migration Tool

[English](README.md)

MOD パックや起動構成を変更する際に、旧インスタンスから新インスタンスへ
Minecraft のユーザーデータを選択的に移行する Windows デスクトップアプリを計画しています。

**現在は Phase 3.4 の Durable Execution Journal Storage です。** UI は引き続き Inspector / Preview のみです。
execution / rollback contract に加え、Windows 上で append-only・checksum 付き JSONL journal を durable flush 付きで
永続化できます。Started が durable で terminal record がない場合、再読込時は Uncertain として復元します。
Migration Execute、restore IO、rollback IO は未実装です。

## 目的

設定やワールドなどの個人データを守るため、移行計画を事前確認し、
バックアップと検証を伴う移行を目指します。

Inspect → Plan → Preview / Dry Run → Backup → Execute → Verify → Report

検証失敗時は Diagnose → Rollback → Report を想定しています。
Planner、Preview / Dry Run、Backup Preflight に加えて Backup IO foundation まで実装済みです。
移行 write、restore、rollback は今後実装します。

## アーキテクチャ

- **Domain:** 不変の観測モデル、決定論的な移行計画、Recommended 選択既定値、明示的な conflict 意図。互換性・nested exclusion は今後実装します。
- **Application:** ユースケースとポート。Domain を参照します。
- **Infrastructure:** Windows の属性取得と handle-relative backup。Application / Domain を参照します。
- **App:** WPF の Inspector / Preview View、MVVM ViewModel、依存関係の組み立て。
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
3. Phase 2: 読み取り専用で決定論的な Migration Planner コア。
4. Phase 2.1: Recommended 選択 preset と明示的な Skip / Replace conflict 意図。
5. Phase 2.2: Preview / Dry Run モデルと WPF preview flow。
6. Phase 3.0: Backup Preflight と version 付き manifest draft。filesystem write はなし。
7. Phase 3.1: owned backup root、handle-relative traversal、cancellation、nested reparse rejection、
   post-copy fingerprint verification を備えた Windows Backup IO foundation。
8. Phase 3.2: owner marker、completion manifest、BackupPlan、再計算した tree fingerprint を使う
   completed-backup read-only revalidation。
9. Phase 3.3: version 付き execution-journal contract と deterministic rollback planning。migration write はなし。
10. Phase 3.4: append / flush ordering と crash-tail recovery を備えた durable Windows execution journal storage。
11. 以後は Execute orchestration → Verifier → rollback IO → Report を個別に実装。

ノード単位で Issue / PR を分け、受け入れテストとともに進めます。
旧版由来の移行候補は [migration rules](docs/migration-rules.md) に整理しています。
Phase 2.1 では legacy の Recommended 方向として、既知候補のうち `saves` と `screenshots`
だけを既定 OFF とし、それ以外を選択します。destination conflict は明示的に Skip / Replace
を指定しない限り未解決のままです。Phase 2.2 の UI dry run は Recommended preset を使い、
未解決 conflict をそのまま表示します。旧版 `hanemod-client.json` exclusion、Merge、
size estimate、互換性保証、migration write、restore IO、rollback IO は未実装です。
