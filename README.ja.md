# Minecraft Instance Migration Tool

[English](README.md)

MOD パックや起動構成を変更するときに、旧 Minecraft インスタンスから新インスタンスへ
ユーザーデータを選択的かつ安全に移行する Windows デスクトップアプリです。

**Phase 5.0 release hardening は現在の feature branch に実装済みです。正式リリースには、オリジナルアイコン、repository license、production signing、Hosted dry-run、Phase 5.1 検証がまだ必要です。**
Application が、Inspect、Plan、Preview、Backup 準備 / 実行、Execute を
明示的な session state で接続する product-level workflow を所有するようになりました。
バックエンドには、検証済み Backup、durable execution / rollback-attempt journal、
Windows Copy / Replace、独立 post-write verification、fingerprint guard 付き rollback まで含まれます。

WPF UI から Inspector、Migration Preview、selection / conflict 編集、明示的な実行確認、
必要時の Backup、検証付き Copy / Replace 実行、Recovery 診断、確認付き guarded rollback、
読み取り専用 Migration Report まで操作できます。
選択中の safety workspace に対する最新の容量確認も、実行前の必須条件です。
ModPackTransfer の既存 migration feature set は、追跡可能な compatibility matrix と回帰テストで
確認しています。参照元 commit `e174cdac8229f3e061175a36121d55db01961452` に対して、
migration に関係する behavior を18件棚卸しし、compatibility matrix の **Missing は0件**です。
11候補を維持しつつ、overwrite、path、backup、verification、rollback、error redaction、
capacity の安全性を強化した後継実装です。

Phase 4.6 の検証基準は **通常テスト 429 passed + FlaUI UI smoke 6 passed**、
build は警告0 / エラー0、Hosted `verify` / `ui-smoke` は merge 前に green です。

Phase 5.0 では開発versionを `0.9.0` に固定し、Windows 11 x64向けself-contained folder publish、
portable ZIP、per-user Inno Setup installer、SHA-256 checksum、assembly metadata由来のAbout表示、
least-privilege release workflowを準備します。`v1.0.0` tagや公開releaseは作成しません。

## 現在の実装範囲

バックエンドの実行フローは次の状態まで実装されています。

```text
Inspect
  ↓
Plan
  ↓
Preview / Dry Run
  ↓
Capacity Preflight
  ↓
Backup Preflight
  ↓
Backup IO
  ↓
Backup Revalidation
  ↓
Execution Workspace Safety
  ↓
Durable Execution Journal
  ↓
Live Revalidation
  ↓
Copy / Replace
  ↓
Independent Post-write Verification
  ↓
Durable Applied / Failed evidence
```

検証失敗後の rollback 側も実装済みです。

```text
Execution evidence
  ↓
RollbackPlan
  ↓
Backup Revalidation（Replace のみ）
  ↓
Durable Rollback Started
  ↓
Fingerprint-guarded Rollback IO
  ↓
Applied / GuardRejected / Failed
  ↓
Durable Rollback Attempt evidence
```

プロセス停止後に rollback journal が `Started` だけ残っている場合は
`Uncertain` として復旧し、成功・失敗・再実行可能を推測しません。
Phase 3.8 は **復旧診断を永続化する段階**であり、自動 resume は実装していません。

### Phase 4.0 Application workflow / session

Phase 4.0 では、既存の use case 群を接続する Application 所有の workflow 境界を追加しました。
product-level な session 遷移は `MigrationWorkflow` が管理し、
呼び出し側は `MigrationWorkflowSession` の evidence を読み取れますが、
public API から任意の state を構築したり state / evidence setter を変更したりはできません。

```text
SelectRoots
  ↓
Inspect
  ↓
ConfigurePlan
  ↓
Preview
  ↓
ReadyForBackup
  ↓
BackupReady
  ↓
ReadyForExecution
  ↓
Executing
  ↓
Completed / Cancelled / Blocked / RecoveryRequired
```

独立監査後の Phase 4.0 では、次を固定しています。

- root を選び直すと以前の plan / backup / execution evidence を持ち越さず fresh state へ戻る
- selection / conflict 入力は defensive copy する
- plan を再設定すると downstream の Preview / Backup / Execution evidence を破棄する
- `NeedsDecision` など Ready でない Preview は backup へ進めない
- `BackupPlanStatus.NotRequired` は backup path を捏造せず、backup IO も呼ばずに進行できる
- Replace 用の実 backup が必要な場合だけ backup parent を必須にする
- Execute 前には journal parent を必須にする
- execution から例外や cancellation exception が漏れた場合は保守的に `RecoveryRequired` とする

Phase 4.0 自体では WPF UI や filesystem adapter を追加していませんでした。
Phase 4.1 では selection と Skip / Replace conflict 編集を接続し、Phase 4.2 では確認付き実行を接続しました。

## Phase 4.1 selection / conflict 編集

Migration Preview を Phase 4.0 の workflow に接続し、Recommended を初期値にしつつ各候補を Include / Exclude できるようにしました。
現在の destination conflict には、Domain ですでに定義済みの `Skip` / `Replace` だけを明示的に選べます。Merge はまだありません。
編集内容は **Apply choices** を押すまで pending とし、既存 inspection evidence から plan / preview を再構築します。choice 編集だけでは再 inspection も filesystem write も行いません。
Source / Destination を変更すると以前の choice/session は破棄し、**Reset Recommended** で既定 preset に戻せます。

## Phase 4.2 確認付き実行

Ready な Previewは、pending choiceをすべてApplyし、Safety workspaceを選択した場合だけ実行できます。
Safety workspaceにはdurable execution journalと、Replace時に必要な検証済みbackup artifactを保存します。
filesystem writeの前に専用確認画面でCopy / Replace / Skip件数、Backup要否、Source、Destinationを表示します。

ViewModelは既存`IMigrationWorkflow`をPrepare Backup、Backup、Prepare Execution、Executeの順に進めます。
Copy-only planではworkflowの`NotRequired` backup resultを使い、backup IOを呼びません。
UIはworkflow evidenceから`Completed`、`Blocked`、`Cancelled`、`RecoveryRequired`を表示します。
`RecoveryRequired`では入力編集と再Executeを禁止します。

## Phase 4.3 Recovery 診断と guarded rollback

Execute が `RecoveryRequired` を返すと、Application が durable execution journal を再読込し、
必要な Replace backup を再検証して、既存 Domain の `RollbackPlan` を生成します。UI は journal
を独自解釈せず、Applied / Failed / Uncertain の typed evidence と、rollback可能・blocked・
manual recovery required の区別を表示します。

Rollback は自動開始しません。backend が Ready と判定した plan だけを有効にし、
Delete-created / Restore-backup 件数を示す専用確認画面を必須にします。既存 executor は
destructive mutation より先に durable Started を保存します。UI は attempt evidence を
Recovered、GuardRejected、Failed、Uncertain として表示します。GuardRejected は現在内容が
migration直後のfingerprintと一致しないため変更しなかった状態です。Uncertainは再実行可能と
解釈せず、automatic resumeは引き続き未実装です。

## Phase 4.4 読み取り専用 Migration Report

Application は既存の Preview、Backup、Execution、Verification、Recovery、Rollback evidence を
typed report へ投影します。Completed、Cancelled、Blocked、RecoveryRequired、Recovered、
GuardRejected、Failed、Uncertain を区別し、workflow state の変更や Execute / Rollback の
許可には使用しません。不足・矛盾した evidence は成功結果を捏造せず、report unavailable
として fail closed に扱います。

Report tab には action 件数、backup outcome、verification outcome を表示し、recovery evidence
がある場合だけ recovery 詳細を表示します。Report model は filesystem path と raw exception
message を保持しません。Phase 4.4 は memory 内表示までとし、自動保存とユーザー export は、
owned destination、collision policy、partial-write 対策を定義する将来フェーズへ残します。
詳細は [report](docs/report.md) を参照してください。

## Phase 4.5 Capacity / Free-space Preflight

Backup や移行書込みを始める前に、Application が Copy / Replace の source logical bytes と、
Replace で backup する現在の destination bytes を評価します。Infrastructure は既存の
handle-relative / no-follow Windows traversal で計測し、canonical volume identity と空き容量も
取得します。Destination と safety workspace が同じ物理 volume なら、SUBST 等の alias も含めて
書込みと backup の必要量を合算します。

見積りには logical bytes の 5%、最小 64 MiB、最大 1 GiB の bounded reserve を加えます。
計測不能、unsafe tree、不正値、overflow、cancel は Ready になりません。root、choice、Preview、
safety workspace を変更すると古い結果を破棄し、現在の Preview と workspace に対する Ready 結果が
なければ Execute は無効です。これは明白な容量不足を事前検出するもので、実行時の live validation や
disk-full を含む IO failure 処理は引き続き必要です。詳細は
[capacity preflight](docs/capacity-preflight.md) を参照してください。

## Phase 4.6 ModPackTransfer compatibility closure

Phase 4.6 では参照元 `TaichiServer/ModPackTransfer` の
commit `e174cdac8229f3e061175a36121d55db01961452` を実コードから監査し、
ユーザー操作・migration に関係する behavior 18件を追跡可能な compatibility matrix に整理しました。
`Missing` は0件です。

旧版の `hanemod-client.json` 除外は、Domain が所有する明示的な migration-content rule として実装しました。
directory candidate 配下の全階層で basename を大文字小文字を区別せず判定し、Copy、Replace時の保持、
Backup、Rollback restore、独立 verification / fingerprint、Capacity 計測、Preview、Report で同じruleを
一貫して利用します。似た名前のfileまで誤って除外しません。

安全性を弱める旧版behaviorはそのまま再現せず、より安全なequivalentへ置き換えています。
無条件overwriteは明示的な Skip / Replace、単純なrecursive copyはhandle-relative no-follow traversalとなり、
Replaceでは verified Backup、live revalidation、durable journal、独立verification、
guarded rollback、capacity gateを維持します。

詳細は [ModPackTransfer compatibility matrix](docs/modpacktransfer-compatibility.md) を参照してください。

## UI で現在できること

### Inspector

任意名のローカルフォルダを選択し、直下の既知 11 項目を読み取り専用で観測します。

- `options.txt`
- `config`
- `resourcepacks`
- `shaderpacks`
- `schematics`
- `saves`
- `screenshots`
- `XaeroWaypoints`
- `XaeroWorldMap`
- `itemscroller`
- `g4mespeed`

期待 kind と実際の state を分離して表示します。
Missing / Inaccessible / Unavailable / ReparsePoint などを区別し、
junction / symlink / reparse point は追跡しません。

### Migration Preview / Dry Run

Source / Destination を読み取り専用で再観測し、Recommended preset の MigrationPlan を表示します。

Recommended では `saves` と `screenshots` は既定 OFF です。
既存 destination conflict は UI 上では `NeedsDecision` のまま表示されます。
Preview UI から Skip / Replace を設定し、未解決状態へ戻すこともできます。選択内容は明示的に
Apply したときだけ、root を再 inspection せず plan / preview へ反映されます。

Previewの生成と編集はmetadata-onlyです。実行は明示的な確認後にのみ開始し、既存Application workflowが
live stateを再検証してからwriteします。

## 安全設計

現在の実装は、単純な再帰コピーではなく、失敗時に状態を証明できる migration engine を目指しています。

### Windows filesystem

- ローカルドライブの絶対パスのみを対象
- handle-relative traversal
- retained parent handles
- no-follow reparse policy
- nested junction / symlink / reparse point を fail closed
- lexical root overlap と canonical handle path の両方を検査
- SUBST 等の物理 alias を考慮
- Copy は create-only
- Replace は backup と live state を再検証してから実行
- user path を untrusted input として扱う

### Backup

Replace 対象だけを事前 backup します。

- owned backup root
- owner marker
- version 付き completion manifest
- planned top-level membership の検証
- tree fingerprint の再計算
- nested reparse rejection
- completed backup の read-only revalidation
- failed / cancelled backup を recovery evidence として扱わない

Backup は完全な NTFS clone ではありません。
ACL、alternate data streams、完全な timestamp / metadata fidelity は保証していません。

### Execution journal

実 write の前に `Started` を durable に保存し、
mutation と独立 verifier が成功した後だけ `Applied` を保存します。

```text
Live Revalidation
  ↓
Backup Revalidation（Replace）
  ↓
durable Started
  ↓
single-entry mutation
  ↓
independent verification
  ↓
durable Applied
```

JSONL record は SHA-256 checksum と previous-checksum chain を持ち、
acknowledged record は `Flush(flushToDisk: true)` 後にのみ成功扱いになります。

unterminated final record は torn tail として扱えますが、
newline 済みの malformed / checksum-invalid / state-invalid record は fail closed です。

### Independent verification

mutation の成功フラグだけを信用せず、

```text
source fingerprint #1
destination fingerprint
source fingerprint #2
```

を比較します。

source が途中で変化した場合は `SourceChanged`、
stable source と destination が一致しない場合は `VerificationMismatch` になります。

fingerprint は path を含まず、相対 tree 構造、通常 file stream の bytes、
file / directory count、total bytes、SHA-256 を使用します。

### Guarded rollback

rollback は execution journal の post-write fingerprint を mandatory guard として使います。

- Copy: 現在 destination が execution 時 fingerprint と一致するときだけ削除
- Replace: backup を再検証し、現在 destination が fingerprint と一致するときだけ restore
- destination / backup の nested tree は handle で保持したまま guard と mutation を行う
- migration 後にユーザーや別プロセスが変更したデータは自動で削除・上書きしない
- destructive rollback 開始後の失敗は `RecoveryRequired`

### Durable rollback-attempt journal

Phase 3.8 では rollback 自体の証拠も別 journal に保存します。

```text
NotStarted
   ↓
durable Started
   ↓
guarded rollback IO
   ↓
Applied / GuardRejected / Failed
```

`Started` の後に terminal record が無ければ再読込時は `Uncertain` です。

rollback journal は exact `RollbackPlan` に binding され、
order、name、expected kind、execution operation、rollback action、
post-write fingerprint を検証します。

未知 JSON property、重複 property、必須 field 欠落、checksum / chain / order 不整合は拒否します。
entry name は single Windows name として検証され、絶対パスや path fragment を journal に保存しません。

checksum は accidental corruption の検出用であり、
journal 全体を書き換えて checksum chain を再計算できる攻撃者に対する authentication ではありません。

## アーキテクチャ

```text
App (WPF)
   ↓
Application
   ↓
Domain

Infrastructure
   ↑
Application ports
```

- **Domain** — 観測モデル、MigrationPlan、選択 / conflict policy、backup / execution / rollback policy
- **Application** — use case、orchestration、外部 effect 用 port
- **Infrastructure** — Windows filesystem、backup、journal、mutation、verification、rollback adapter
- **App** — WPF / MVVM、Inspector / Preview / selection / conflict / Execute / Recovery Diagnosis / Guarded Rollback / Migration Report UI、composition root
- **Tests** — Domain / Application / Infrastructure / App / FlaUI UI smoke

依存は内向きです。
Application / Domain は Infrastructure や UI を参照しません。

採用技術:

- C#
- .NET 10
- WPF
- MVVM
- System.IO / Windows native filesystem APIs
- System.Text.Json
- xUnit v3
- FlaUI
- GitHub Actions

production code では不要な DI / MVVM / logging framework を追加せず、
BCL と明示的な port / adapter を中心に構成しています。

詳細は [architecture](docs/architecture.md) を参照してください。

## インストール準備状況

pipelineは次の固定artifact名を生成します。

- `MinecraftInstanceMigrationTool-0.9.0-win-x64.zip`
- `MinecraftInstanceMigrationTool-0.9.0-win-x64-setup.exe`
- `SHA256SUMS.txt`

安定版downloadはまだありません。Phase 5.1でartifactを検証してから`v1.0.0`へ進みます。
installerはper-user、ZIPはportableで、どちらもself-containedです。checksum、署名、upgrade、
support範囲は [install](docs/install.md) と [release process](docs/release.md) を参照してください。

このアプリは非公式のcommunity toolで、Mojang StudiosまたはMicrosoftとの提携はありません。

## 現在未実装のもの

以下はまだ完成扱いではありません。

- report persistence / ユーザー操作による export
- automatic rollback resume
- Merge conflict semantics
- Minecraft / mod / loader compatibility 判定
- exact NTFS clone semantics
- production Authenticode signingとstable release公開
- original application iconとowner-selected repository license
- clean-machine install / upgrade / migration検証

特に、**「コピーできる」ことと「新インスタンスで互換性がある」ことは別です。**
現在の実装は compatibility を保証しません。

## 開発環境

Windows と [global.json](global.json) 指定の .NET 10 SDK
（10.0.401、同じ feature band の最新 patch を許容）が必要です。

```powershell
dotnet restore
dotnet build --configuration Release --no-restore
dotnet run --project src/MinecraftInstanceMigration.App --configuration Release --no-build
```

ライブラリは `net10.0`、WPF host は `net10.0-windows` です。
依存 restore には NuGet 接続が必要ですが、アプリ自体に network 機能はありません。

## 検証

通常の deterministic gate:

```powershell
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build --no-restore
dotnet format --verify-no-changes --no-restore
git diff --check
git status --short --branch
```

WPF UI smoke:

```powershell
dotnet test tests/MinecraftInstanceMigration.UiTests/MinecraftInstanceMigration.UiTests.csproj --configuration Release --no-restore
```

GitHub Actions は Windows 上で build / test / format と UI smoke を実行します。
warnings は build failure として扱います。

高リスクな filesystem write / rollback のテストは、owned temporary fixture のみを使用します。
実 Minecraft instance を test fixture として変更しません。

詳細は [testing](docs/testing.md) を参照してください。

## ロードマップ

実装済み:

1. Phase 0 — solution / layer boundary / test / CI / minimal WPF shell
2. Phase 1 — read-only Instance Inspector
3. Phase 2 — deterministic Migration Planner
4. Phase 2.1 — Recommended preset + Skip / Replace conflict intent
5. Phase 2.2 — Preview / Dry Run + WPF preview
6. Phase 3.0 — Backup Preflight
7. Phase 3.1 — Windows Backup IO
8. Phase 3.2 — completed-backup revalidation
9. Phase 3.3 — execution journal / rollback contract
10. Phase 3.4 — durable execution journal
11. Phase 3.5 — live revalidation + execute orchestration
12. Phase 3.6 — Windows Copy / Replace + independent verification
13. Phase 3.7 — guarded rollback IO
14. Phase 3.8 — durable rollback-attempt journal
15. Phase 4.0 — Application 所有の migration workflow / session
16. Phase 4.1 — selection / conflict 編集 UI
17. Phase 4.2 — 確認付き end-to-end Execute UI
18. Phase 4.3 — Recovery Diagnosis / Guarded Rollback UI
19. Phase 4.4 — 読み取り専用 Migration Report
20. Phase 4.5 — Capacity / Free-space Preflight
21. Phase 4.6 — ModPackTransfer compatibility closure

次の大きな領域:

22. **Phase 5.0 — Release hardening（実装済み、release blockerとHosted evidence待ち）**
23. Phase 5.1 — v1.0 release validation

調査した旧版機能と回帰証拠は [ModPackTransfer compatibility matrix](docs/modpacktransfer-compatibility.md)、
移行候補と rule は [migration rules](docs/migration-rules.md)、
rollback の保証範囲は [rollback](docs/rollback.md)、
execution evidence は [execution journal](docs/execution-journal.md) を参照してください。
Report projection と persistence 境界は [report](docs/report.md) を参照してください。

開発ルールは [AGENTS.md](AGENTS.md)、
検証方針は [testing](docs/testing.md)、
Evidence-driven Graph Loop は [graph loop](docs/graph-loop.md) を参照してください。
