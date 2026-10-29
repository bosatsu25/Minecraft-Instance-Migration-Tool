# 2026-10 アーキテクチャ・セキュリティ・配布レビュー

- 対象: v1.0.0（`3ec11ad`）の全ソース、CI/CD、インストーラー、配布手順
- 判断記録: [ADR 0003](../adr/0003-filesystem-kernel-and-supply-chain.md)
- 確信度の表記: **確実**（コード・ツール出力・公式記述で確認）／**可能性が高い**（強い推論）／**推測**（情報不足）

## 1. 結論（耳の痛い点から）

1. **全ソースの作り直しは行っていません。行うべきでもありません。**（可能性が高い）
   v1.0.0 は fail-closed・指紋ガード付きロールバック・耐久ジャーナルという、作り直すと失われやすい性質を
   458件の決定的テストと10件のUIテストで固定しています。JSTQB のリスクベースドテストの考え方に従い、
   「欠陥・不整合・重複が実在する箇所」だけを再設計しました。
2. **配布時の最大のユーザー負担は技術選定ではなく「未署名」です。**（確実）
   SmartScreen／発行元不明の警告は残ります。`SHA256SUMS.txt` は同じ場所に置かれる限り、配布物が
   改ざんされていないことの証明にはなりません。今回はビルド来歴証明（attestation）を追加しましたが、
   これも SmartScreen の警告は消しません。
3. **計画段階で弾くべき欠陥が1件ありました（D1）。** データ破損には至りませんが、利用者には
   「復旧が必要」という誤った不安を与える経路でした。

## 2. テストベースと手法

| JSTQB の観点 | 適用内容 |
| --- | --- |
| 静的テスト（レビュー） | 全 src（約15k行）を精読。重複・ドリフト・例外分類・権限要求・キャンセル経路を確認 |
| 静的解析ツール | `zizmor`（Actions セキュリティ）、`actionlint`、コンパイラ警告（警告=エラー）、`dotnet format` |
| リスクベースドテスト | 影響×発生可能性で D1〜D8 を優先付け（下表） |
| 同値分割・境界値分析 | パス規則 47ケース（長さ 32000/32001、ドライブルート、UNC/デバイス/相対、`..`、ADS、制御文字） |
| デシジョンテーブル | 計画ポリシー：移行元種類 × 移行先状態 × 決定（Replace/Skip/未決定） |
| 状態遷移テスト | ジャーナル（NotStarted→Started→Applied/Failed、Started のみ→Uncertain）の不正形状3ケース |
| 回帰（互換性）テスト | 永続化済み指紋のゴールデン値を、旧実装の符号化から独立に算出して固定 |
| 否定対照 | 修正を外すと新テストが失敗する（RED）ことを確認してから修正版で GREEN |

## 3. 検出事項

| ID | 重大度 | 内容 | 根拠 | 対応 |
| --- | --- | --- | --- | --- |
| D1 | 高（利用者影響） | 移行先の項目が想定と違う種類（例：`config` がファイル）でも Replace が計画・プレビュー・バックアップを通過し、実行ポートが Started 記録**後**に必ず拒否 → `RecoveryRequired` | `MigrationPlanPolicy` は移行先種類を見ず、`WindowsExecutionMutationPort.EnsureReplaceDestination` は期待種類を要求（確実） | 計画で `BlockedDestinationKindMismatch`。Skip は可能。UI から Skip へ変更可能に |
| D2 | 中（保守性・セキュリティ） | セキュリティ検証ロジックの複製：単一名検証×4、パス正規化×4、ディレクトリチェーン×3、P/Invoke×3、重複判定×5、指紋アルゴリズム×2、ジャーナル機構×2 | grep で実数を確認（確実） | `Infrastructure/FileSystem` に単一化。Infrastructure 6,747→4,891行 |
| D2a | 中 | 複製間の**実際のずれ**：バックアップのルート処理だけ非ディレクトリ要素を拒否しない／Inspector だけドライブ判定の例外を捕捉しない／実行ジャーナルだけ JSON 厳格検証なし | 各コピーの比較（確実） | 単一実装で解消。厳格化は旧ジャーナルの読込互換を維持 |
| D3 | 低（潜在） | ロールバックの復元で宛先削除**後**にキャンセルされると「GuardRejected＝宛先未変更」と誤報告される経路 | `RestoreFromBackup` に OCE の捕捉なし（確実）。現状は呼び出し側が `CancellationToken.None` を渡すため到達不能（確実） | 削除開始後のキャンセルは RecoveryRequired として報告 |
| D4 | 中（サプライチェーン） | Actions 未ピン留め14件、認証情報の永続化7件、テンプレート注入3件、`pull_request_target` で fork PR の内容を外部 LLM に送る経路 | zizmor 25件（確実） | SHA ピン留め、`persist-credentials: false`、環境変数経由、fork 除外。zizmor 0件 |
| D5 | 中 | 署名用シークレットを `GITHUB_ENV` に書き出し、後続のテスト（サードパーティ依存を実行）にも露出。署名ステップ内でも、署名後のアプリ起動確認が環境変数を継承 | release.yml、`Build-Release.ps1`（確実） | 署名ステップのみに限定し、`Build-Release.ps1` が最後の署名後・アプリ起動前に除去 |
| D6 | 低 | NuGet ロックファイルなし・取得元未固定（依存関係の取り違え余地） | リポジトリ構成（確実） | テスト5プロジェクトにロック＋`--locked-mode`、`nuget.config` で nuget.org のみに固定、Dependabot |
| D7 | 低 | 日本語 UI で Blocked 系の理由が英語識別子のまま表示 | `UiText.Translate` の語境界規則（確実） | 全 Blocked 系に日本語を追加しテストで網羅 |
| D8 | 低（性能） | 検証時のディレクトリ名照合が O(n²)（大きな `saves` で遅い） | `MigrationPayloadMatches`（確実） | 辞書照合に変更（同順位の選択規則は維持） |
| D9 | 中（追補 2026-10-29） | 置換の直前に「現在の移行先がバックアップと同じ内容か」を確認していない。バックアップ後に移行先が変わると、その変更は削除され、バックアップにも残らない | `ExecutionOrchestrator` と `ExecutionLiveValidationPolicy` は上位の種類とバックアップの完全性だけを確認（確実） | [ADR 0004](../adr/0004-merge-existing-destination-directories.md) §6 で対策を設計（未実装） |

## 4. 採用技術の評価（ユーザー負担・導入/削除の容易さ）

| 技術 | 判定 | 理由 |
| --- | --- | --- |
| .NET 10 LTS 自己完結 | 維持 | 利用者は .NET の導入不要（確実）。LTS は 2028年11月まで（確実）。代償は配布サイズ |
| WPF | 維持 | Windows 専用製品で追加ランタイム不要。WinUI 3 は Windows App SDK の配布が増え、Avalonia は必要のない依存を増やす（可能性が高い） |
| 非 single-file・非トリミング | 維持 | WPF のトリミングは非対応（確実）。single-file は WPF のネイティブ DLL を一時フォルダーへ展開するため、削除後の残骸やウイルス対策の誤検知が増えやすい（可能性が高い） |
| Inno Setup ユーザー単位インストーラー＋ZIP | 維持 | 管理者権限不要、CI がインストール・再インストール・残骸なしのアンインストールを検証済み（確実）。MSIX は信頼された署名が必須（確実） |
| NT API のハンドル相対操作 | 維持（単一化） | 再帰削除/コピーでジャンクションを辿る事故と TOCTOU のパス差し替えを構造的に防ぐ（確実）。複雑さの代償は D2 の単一化で軽減 |
| `DllImport` | 維持 | `LibraryImport` は unsafe 有効化と再マーシャリングが必要で、AOT/トリミングを使わない本製品では利益が小さい（可能性が高い）。検索パスは System32 に限定 |
| 未署名配布 | **最大の課題** | 下記 6 を参照 |

導入・削除の容易さ：インストーラー版はスタートメニュー登録とアンインストールが標準手順で完結し、CI が残骸を検査しています（確実）。
アプリのコードは設定・ログ・テレメトリを書きません（確実：コードで確認）。残るのは利用者が選んだ保存先の `mim-backup-*` と
`mim-journal-*.jsonl` だけで、これは復旧証拠なので意図的に自動削除しません。ZIP 版は「ZIP 全体を展開」が必要な点が摩擦です。

## 5. 実施した再設計

- `Infrastructure/FileSystem` を単一カーネル化：`NtFileApi`（全 P/Invoke）、`WindowsPathRules`（OS 非依存で検証可能な字句規則）、
  `FileSystemErrors`（例外・NTSTATUS の単一分類）、`ChecksummedJournal`（両ジャーナル共通の耐久書込み機構）。
- `WindowsExecutionTree` を唯一の走査・コピー・削除・指紋実装とし、バックアップの独自コピー（約500行）を削除。
- 各ハンドルは必要最小限の権限のみ要求（例：ジャーナル親フォルダーに削除権限を要求しない）。
- 計画段階で種類不一致の Replace を拒否（D1）。
- CI/CD を強化（D4〜D6）し、タグリリースにビルド来歴証明を追加。

## 6. 推奨事項（優先度順、本変更の範囲外）

1. **コード署名**：オープンソース向けの無償署名プログラム（SignPath Foundation など、審査あり：推測）か、
   Azure Artifact Signing（旧 Trusted Signing、月額、個人の地域要件あり：推測）。署名しても SmartScreen の評判は即時には付かない点に注意（可能性が高い）。
2. **winget 登録**：`winget install/upgrade/uninstall` で導入・更新・削除が1コマンドになる。外部公開なので人の判断が必要。
3. `MigrationPreviewViewModel`（1,108行）を選択編集・容量・実行・復旧に分割。
4. ローカライズを「英語文の置換」から「キー＋リソース」へ移行。
5. Domain にカバレッジ計測とミューテーションテスト（Stryker.NET）を導入し、テストの検出力を数値化。
6. Application の広い `catch (Exception)` に、パスを含まない診断カテゴリを残す（現在は安全側に倒すが原因が消える）。

## 7. 検証結果

Linux 上で実測（Windows 専用の Infrastructure/App/UI テストは CI で確認）：

| 検証 | 結果 |
| --- | --- |
| `dotnet build -c Release`（警告=エラー） | 成功、警告0・エラー0 |
| Domain.Tests | 115/115 合格（新規5） |
| Application.Tests | 136/136 合格 |
| WindowsPathRulesTests | 47/47 合格 |
| App 表示ロジック（WPF 非依存部分） | 5/5 合格 |
| ReleaseConfigurationTests（ワークフロー契約） | 9/9 合格 |
| `dotnet restore --locked-mode` / `dotnet format --verify-no-changes` | 成功 |
| zizmor / actionlint | 25件→0件 / 0件 |
| 否定対照（D1 修正を外す） | 新テスト2件が失敗することを確認 |
