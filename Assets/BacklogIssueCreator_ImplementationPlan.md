# Backlog 課題作成機能 実装計画

## 1. curl テスト結果

### 1.1 GET priorities（優先度一覧取得）✅ 成功

```bash
curl --request GET \
  --url "https://aiwh60018.backlog.com/api/v2/priorities?apiKey=zSmIZrqgKib5AUQnj9hrweuOEgtwJrzxVvqSdYkcGnKYDAoN8YNTIw8Qh4fiVjZH"
```

**レスポンス:**
```json
[
  {"id":2,"name":"高"},
  {"id":3,"name":"中"},
  {"id":4,"name":"低"}
]
```

### 1.2 POST issues（課題作成）✅ 成功

```bash
curl --request POST \
  --url "https://aiwh60018.backlog.com/api/v2/issues?apiKey=zSmIZrqgKib5AUQnj9hrweuOEgtwJrzxVvqSdYkcGnKYDAoN8YNTIw8Qh4fiVjZH" \
  --header 'Content-Type: application/x-www-form-urlencoded' \
  --data projectId=746593 \
  --data summary="テスト課題作成" \
  --data issueTypeId=3984946 \
  --data priorityId=3
```

**レスポンス:** 課題 NULABCUP-3 が正常に作成されました。

---

## 2. プロジェクト構成の把握

### 2.1 UI コンポーネント

| コンポーネント名 | 役割 | 親オブジェクト |
|----------------|------|---------------|
| **TextInputField** | 課題の概要（summary）入力 | 964791678 |
| **DropDown1LineTextOnly** | 優先度（priority）選択 | 964791678 |
| **PrimaryButton_IconAndLabel_UnityUIButton** | 課題作成ボタン（「Issue」ラベル） | 964791678 |

- これらは **QDS (Quick Design System)** 由来の UI コンポーネント（Meta XR SDK）
- `m_OnClick` から、PrimaryButton は **Unity uGUI (Button)** ベースと推測
- DropDown は **TMP_Dropdown** または類似の uGUI ドロップダウン

### 2.2 既存スクリプト

- `ThrowableCube.cs`, `CubeSpawner.cs`, `LeftHandDebugPanel.cs` など
- 現時点で Backlog 連携用のスクリプトは存在しない

---

## 3. 実装計画

### Phase 1: Backlog API クライアント基盤

1. **`BacklogApiClient.cs`** を作成
   - `UnityWebRequest` で HTTP GET/POST を実行
   - エンドポイント・API Key は `ScriptableObject` または `[SerializeField]` で設定
   - 非同期処理: `UnityWebRequest.SendWebRequest()` + コルーチン または `async/await`（.NET 4.x 対応時）

2. **データモデル**
   - `BacklogPriority` クラス: `id`, `name`
   - `BacklogIssueCreateRequest` クラス: `projectId`, `summary`, `issueTypeId`, `priorityId`

### Phase 2: DropDown の動的オプション設定

1. **`BacklogIssueCreator.cs`** を作成
   - `[SerializeField] TMP_Dropdown m_PriorityDropdown;` で DropDown1LineTextOnly を参照
   - `Start()` または `Awake()` で Backlog API から priorities を取得
   - 取得した `{id, name}` を `Dropdown.OptionData` に変換して `m_PriorityDropdown.options` に設定
   - 表示は `name`、実際の値は `id` を保持するため、`options` のインデックスと `priorities` リストの対応を保持

### Phase 3: ボタンクリックで課題作成

1. **PrimaryButton の OnClick に `BacklogIssueCreator.CreateIssue()` を紐付け**
   - Unity エディタで Button の OnClick に `BacklogIssueCreator` の `CreateIssue` メソッドを割り当て
   - または `BacklogIssueCreator` 内で `Button.onClick.AddListener(CreateIssue)` で動的登録

2. **`CreateIssue()` の処理**
   - `TextInputField`（TMP_InputField または類似）から `summary` を取得
   - `DropDown1LineTextOnly` の選択インデックスから `priorityId` を取得
   - `projectId=746593`, `issueTypeId=3984946` は固定または設定可能に
   - `BacklogApiClient` で POST を実行
   - 成功時: 入力欄クリア、トースト/ログで通知
   - 失敗時: エラーメッセージをログまたは UI に表示

### Phase 4: 設定の外部化・セキュリティ

1. **API Key の管理**
   - ⚠️ **重要**: API Key をソースコードにハードコードしない
   - `Resources/BacklogSettings.asset` のような ScriptableObject に格納し、`.gitignore` で除外
   - または環境変数・PlayerPrefs（開発時のみ）で読み込み

2. **定数**
   - `projectId`, `issueTypeId` を Inspector で設定可能にする

---

## 4. 実装タスク一覧

| # | タスク | 優先度 |
|---|--------|--------|
| 1 | `BacklogApiClient.cs` 作成（GET/POST ラッパー） | 高 |
| 2 | `BacklogPriority` 等のデータモデル定義 | 高 |
| 3 | `BacklogIssueCreator.cs` 作成 | 高 |
| 4 | DropDown に priorities を動的設定 | 高 |
| 5 | PrimaryButton の OnClick で CreateIssue 呼び出し | 高 |
| 6 | TextInputField 参照と summary 取得 | 高 |
| 7 | API Key 等の設定を ScriptableObject 化 | 中 |
| 8 | エラーハンドリング・ユーザーフィードバック | 中 |

---

## 5. 注意事項

- **API Key の取り扱い**: リポジトリにコミットしないこと。本計画書にも実際の運用時はマスクすることを推奨。
- **Unity の .NET バージョン**: `UnityWebRequest` は標準で利用可能。`async/await` を使う場合はプロジェクトの API レベルを確認。
- **QDS コンポーネントの型**: DropDown1LineTextOnly が `TMP_Dropdown` か `Dropdown` (uGUI) か、実際のプレハブを確認してから参照型を確定する必要あり。
