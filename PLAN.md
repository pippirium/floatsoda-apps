# floatsoda-apps 移植計画

ChromeView(Chrome / デスクトップ画面の VR オーバーレイ共有)と WristAgent(腕への
エージェント承認 UI)を、FloatSoda フレームワークから独立したアプリリポジトリとして
切り出すための計画。

## 1. 目的と位置づけ

| レイヤ | 場所 | 変更の流れ |
|---|---|---|
| フレームワーク(エンジン部分) | 本リポジトリ内の `engine/`(git submodule) | 上流 `sumx21t/FloatSoda` の変更を submodule 更新で取り込む。自前の修正は fork に push し、上流へ PR |
| アプリ機能部分 | 本リポジトリ(WristAgent / ChromeView / bridge tool) | ここで自由に開発。エンジン非改変で済むものはここに集約 |

原則: **エンジンの中で発見した不具合・新機能は上流(FloatSoda)へ PR で戻し、
本リポジトリは submodule のコミット更新によってそれを取り込む**。
本リポジトリにエンジンの差分を直接持たないため、上流追従が常に1コマンドで済む状態を保つ。

## 2. 全体構成(目標形)

```text
floatsoda-apps/
├── PLAN.md                    ← 本書
├── README.md                  ← アプリ全体の説明・起動方法・エージェント接続手順
├── .gitignore                 ← .NET 標準(floatsoda リポジトリから流用)
├── engine/                    ← git submodule → fork(t22m003/FloatSoda)
│   └── (upstream 全体: src/ tests/ docs/ samples/)
├── apps/
│   ├── WristAgent/            ← FloatSoda.Samples.WristAgent から移植
│   └── ChromeView/            ← FloatSoda.Samples.ChromeView から移植
│       (OverlayManipulator を含む)
└── tools/
    └── WristAgentBridge/      ← hooks 中継 + stdio MCP サーバー
```

- `engine/` は上流リポジトリ全体をサブモジュール化する(src/ だけの部分木分割は
  `git subtree split` の維持コストが高いため)。tests/ や docs/ も入るが無害。
- `apps/` と `tools/` はソリューション(仮称 `FloatSoda.Apps.slnx`)に登録し、
  `csproj` の `ProjectReference` は `../../engine/src/FloatSoda/...` を指す。

## 3. エンジン取り込み方式の決定

| 方式 | 上流取り込み | IDE/ビルド | 採否 |
|---|---|---|---|
| **submodule**(採用) | `git submodule update --remote` で HEAD 追従 → pin コミットを更新 | 正常動作。Rider は submodule も1窓で見える | ✅ |
| subtree | `git subtree pull --squash`。tests・docs・samples も全量マージされ競合抵抗が継続発生 | 同 | ❌(ノイズ大) |
| NuGet パッケージ参照 | バージョン更新のみ。**エンジンの未リリース修正(ControllerRayPointerSource など)が使えない** | 同 | ❌(現段階) |

submodule は「fork の branch を 1 本固めて pin する」という運用になる:

```bash
# 上流の取り込み
cd engine
git checkout main
git fetch origin && git merge --ff-only origin/main   # 上流 = sumx21t/FloatSoda
# 自前修正は fork へ(必要なら上流へ PR)
cd ..
git add engine && git commit -m "chore(engine): submodule を <tag/commit> へ更新"
```

## 4. 移植手順

### Phase A — 下支え(エンジン側)

1. fork(`t22m003/FloatSoda`)の `main` に、実装済みブランチを統合する:
   - `182-device-tracked-pointer`(ControllerRayPointerSource / OverlayKeyboard /
     FloatSodaApp.Post / WristAgent・bridge の現物がまだこっちにある)
   - `chrome-view-sample`(WGC 画面キャプチャ)
   - 競合想定なし(着手ファイルが別)だが、統合後に `dotnet build` + 全テストを実行
2. **この統合が済んだら、フレームワーク側の新機能は上流 `sumx21t/FloatSoda` へ PR**
   - 対象: `ControllerRayPointerSource` ほか Phase 1 の本体変更、`OverlayKeyboard`、
     `FloatSodaApp.Post`、docs 更新
   - WristAgent / ChromeView のサンプル・tools は**上流 PR に含めない**か、
     含めるなら「サンプル」として最小限に留める(置き場所が本リポジトリに決定済みのため)
3. submodule登録:
   ```bash
   git submodule add https://github.com/t22m003/FloatSoda.git engine
   cd engine && git checkout <統合済みの commit> && cd ..
   git commit -m "chore(engine): FloatSoda を pin"
   ```

### Phase B — アプリ部分の移植

4. `apps/WristAgent/`:
   - `samples/FloatSoda.Samples.WristAgent/` から csproj・ソース一式をコピー
   - **まずはコード改変なし**で動かす(移植差異を最小化するため)。動作確認後に改名を検討
   - ProjectReference を `../../engine/src/FloatSoda/FloatSoda.csproj` へ修正
   - slnx 登録
5. `apps/ChromeView/`:
   - `samples/FloatSoda.Samples.ChromeView/`(Program.cs + `OverlayManipulator.cs`)を移植
   - こちらもコード改変なし
6. `tools/WristAgentBridge/`: そのままコピー(外部依存なし)
7. README・README 内のパス・設定例(`dotnet run --project ...` のパス)を新構成へ書き換え

### Phase C — 動作確認と整地

8. `dotnet build` — submodule の参照解決を確認
9. 実機確認(SteamVR 起動中、HMD 着用):
   - ChromeView: 画面がオーバーレイに出る
   - WristAgent: 腕にカード・レーザー選択・OSK 入力・振動・ホットキー
   - 併用: ChromeView を見ながら腕で承認要求を返せるか
10. 移植後は元 FloatSoda リポジトリの `samples/FloatSoda.Samples.WristAgent` と
    `tools/WristAgentBridge` を削除し、「#182 実装」は上流 PR のみを残す方針で
    FloatSoda 側をきれいにする(重複が残るとどちらが正か曖昧になる)
11. CI: `dotnet build` のみの軽い CI を追加(SteamVR 必須の実機テストは手動運用)

## 5. 注意点(移植で壊れやすい箇所)

- **オーバーレイキーがアセンブリ名由来**(`WindowKeyGenerator`):
  プロジェクト名を変えるとオーバーレイキーが変わり、`WristVibrator` の
  `FindOverlay` が見つからなくなる。第一段階は**アセンブリ名を変えない**
  (`FloatSoda.Samples.WristAgent` のまま `apps/` に置く)。
  改名する場合は `AssemblyName` を明示指定してキーを固定する。
- **namespace と物理ディレクトリの一致**が本リポジトリの規約にもなるので、
  改名するなら namespace もセットで(Rider の semantic rename 推奨)。
- `Dpm` / `AppKey` 等の初期化値はそれぞれのサンプルの現在値を踏襲。
- docs(Input.md など)は engine 側(上流 FloatSoda)に属する。本リポジトリには
  アプリ固有の README・エンドユーザー向け接続手順のみ置く。

## 6. 引き継ぎ事項(移植中も閉じない)

- 実機検証未実施: UV→ピクセルの Y 反転の仮定(反転なし)ほか、
  レーザー選択/OSK/振動/ホットキーの実測(元 repo と本リポジトリで同時に行わない)
- 音声入力(yes/no 手ぶくみ)は計画のみ。本リポジトリの Phase 相当として引き継ぐ
- hooks 運用は変更のたびに `/hooks` の trust review が必要
- OSK は SteamVR 全体で同時 1 セッション(`KeyboardAlreadyInUse` 時は TryOpen が false)
- MCP キャッシュ: bridge の `serve-mcp` は Codex/Claude Code の `mcp_servers` 登録が必要
