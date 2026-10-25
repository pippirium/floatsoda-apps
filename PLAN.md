# floatsoda-apps 移植計画

ChromeView(Chrome / デスクトップ画面の VR オーバーレイ共有)と WristAgent(腕への
エージェント承認 UI)を、FloatSoda フレームワークから独立したアプリリポジトリとして
切り出すための計画。

## 1. 目的と位置づけ

| レイヤ | 場所 | 変更の流れ |
|---|---|---|
| フレームワーク(エンジン部分) | 本リポジトリ内の `engine/`(git submodule)→ **上流 `sumx21t/FloatSoda` を直接 pin** | submodule チェックアウト後に本リポジトリ持添の `patches/` を適用して、必要なエンジン変更を本リポジトリ内で完結させる |
| アプリ機能部分 | 本リポジトリ(WristAgent / ChromeView / bridge tool) | ここで自由に開発 |

**重要**: 上流・fork への PR / push は一切行わない。fork を使わない(submodule は
上流 URL を直接指す)。これにより fork 上に PR が紛れても上流に通知が飛ぶ
トラブル自体が構造的に発生しない。エンジンの自前変更はすべて
`patches/` の差分ファイルとして本リポジトリだけに持ち、submodule の
ワーキングツリーに適用する(submodule 内ではコミットしない)。

## 2. 全体構成(目標形)

```text
floatsoda-apps/
├── PLAN.md                    ← 本書
├── README.md                  ← アプリ全体の説明・起動方法・エージェント接続手順
├── .gitignore                 ← .NET 標準(floatsoda リポジトリから流用)
├── engine/                    ← git submodule → sumx21t/FloatSoda(upstream を直接 pin)
├── patches/                   ← エンジンへの自前差分(git format-patch 出力)
│   ├── 0001-...-controller-ray-pointer-source.patch   (#182 本体 + OverlayWindow 一般化 + テスト + docs)
│   ├── 0002-...-overlay-keyboard.patch                (OSK ラッパー)
│   └── 0003-...-floatsoda-app-public-post.patch       (FloatSodaApp.Post)
├── scripts/
│   └── setup.ps1              ← submodule checkout + patch 適用(初回/上流更新のたび)
├── apps/
│   ├── WristAgent/            ← FloatSoda.Samples.WristAgent から移植
│   └── ChromeView/            ← FloatSoda.Samples.ChromeView から移植
│       (OverlayManipulator を含む)
└── tools/
    └── WristAgentBridge/      ← hooks 中継 + stdio MCP サーバー
```

- `apps/` と `tools/` はソリューション(仮称 `FloatSoda.Apps.slnx`)に登録し、
  `csproj` の `ProjectReference` は `../../engine/src/FloatSoda/...` を指す。
- patch 適用は working tree への変更として行い、**submodule HEAD は常に上流 pin**。
  submodule が dirty に見える問題は `.gitmodules` に `ignore = dirty` を入れて無視

## 3. エンジン取り込み方式の決定

| 方式 | 上流取り込み | IDE/ビルド | 採否 |
|---|---|---|---|
| **submodule + patch 系列**(採用) | `git -C engine fetch upstream` → pin 更新 → patch の再適用を確認 | 正常動作。Rider は submodule も1窓で見える | ✅ 上流への送信が一枚も発生しない |
| fork main に差分を持たせ pin | fork push(+ 間接的な上流通知リスク) | 同 | ❌ PR/通知を避けたい |
| subtree | `git subtree pull --squash` で全量マージ、競合抵抗が継続 | 同 | ❌(ノイズ大) |
| NuGet パッケージ参照 | バージョン更新のみ。**未リリースのエンジン修正(ControllerRayPointerSource など)が使えない** | 同 | ❌(現段階) |

### submodule + patch の運用

```powershell
# 初回と上流更新のたび(scripts/setup.ps1 の中身)
git submodule update --init --checkout engine
git -C engine apply --3way --whitespace=nowarn patches/*.patch
```

submodule HEAD を新たな上流コミットへ上げた後は patch が当たらない可能性がある。
その場合の手順:

1. **patch の再生** — engine worktree 内で一時 branch を作り、旧 pin に旧 patch を
   `git am` で commit 形にして、新 pin へ `git rebase`。その後
   `git format-patch` で差分を書き戻し、本リポジトリの `patches/` を差し替える
2. ビルド + 全テスト(`tests/FloatSoda.Test`, `tests/FloatSoda.Rendering.Test`)を実行してから
   pin を `git add engine` でコミット

patch の送信元は一貫しており、本リポジトリのコミット履歴にある差分だけが
エンジンへの情報源。上流リポジトリ側に何もないので、上流がどう進んでいても
本リポジトリのビルドは壊れない。

## 4. 移植手順

### Phase A — patch 系列の生成と engine 取り込み

1. 既存 FloatSoda ワーキングコピー(ローカル)から、エンジン宛の差分を取り出す。
   基点は**上流 main の現在 HEAD**(ChromeView のベースになっているコミット)
   ```bash
   # 182-device-tracked-pointer ブランチ側から
   git format-patch <upstream-main-HEAD>..HEAD -- src tests docs FloatSoda.slnx
   ```
   - `samples/` と `tools/` は**patch 対象外**(本リポジトリへはコピペで移すため)
   - 生成した patch は本リポジトリの `patches/` へ
2. 本リポジトリに `.gitignore`, `scripts/setup.ps1`, `.gitmodules`(submodule 登録)を用意
   ```bash
   git submodule add https://github.com/sumx21t/FloatSoda.git engine
   # pin は上流 main HEAD。docs/ tests/ も入るが無害
   ```
3. `scripts/setup.ps1` を実行し、patch がきれいに当たること & `dotnet build` +
   テストが通ることを確認 → pin と patch、scripts をコミット

### Phase B — アプリ部分の移植

4. `apps/WristAgent/`:
   - `samples/FloatSoda.Samples.WristAgent/` から csproj・ソース一式をコピー
   - **まずはコード改変なし**で動かす(移植差異を最小化するため)。動作確認後に改名を検討
   - ProjectReference を `../../engine/src/FloatSoda/FloatSoda.csproj` へ修正
   - slnx 登録
5. `apps/ChromeView/`:
   - `samples/FloatSoda.Samples.ChromeView/`(Program.cs + `OverlayManipulator.cs`)を移植
   - エンジン変更が要る箇所が見つかれば、その差分を `patches/` に追加する
   - こちらも基本的にコード改変なし
6. `tools/WristAgentBridge/`: そのままコピー(外部依存なし)
7. README・README 内のパス・設定例(`dotnet run --project ...` のパス)を新構成へ書き換え

### Phase C — 動作確認と整地

8. `dotnet build` — submodule の参照解決を確認
9. 実機確認(SteamVR 起動中、HMD 着用):
   - ChromeView: 画面がオーバーレイに出る
   - WristAgent: 腕にカード・レーザー選択・OSK 入力・振動・ホットキー
   - 併用: ChromeView を見ながら腕で承認要求を返せるか
10. 元 FloatSoda リポジトリ(ローカル)側のブランチは残してよい(patch の安全性担保と
    履歴参照用)。以後の開発は本リポジトリ側が正。fork / 上流には何も送らない
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
- docs(Input.md など)は engine 側に属するが、patch 時の年次的な考え方として
  「上流に伝えたい事実」は本リポジトリのドキュメントでも記述しておく
  (上流へは PR を出さないため、上流への乖離のファクトは本リポジトリ内に記録)。
- patch は engine/submodule HEAD 上の差分を前提としているため、
  **patch 適用前の engine は「まっさらな上流 pin」であることを崩さないこと**
  (patch 済み engine に対して再適用を試みると失敗する。setup.ps1 は `--check` 付き)。

## 6. 引き継ぎ事項(移植中も閉じない)

- 実機検証未実施: UV→ピクセルの Y 反転の仮定(反転なし)ほか、
  レーザー選択/OSK/振動/ホットキーの実測(本リポジトリの apps/ 上で確認)
- 音声入力(yes/no を声で答える)は計画のみ。本リポジトリ側の拡張として引き継ぐ
- hooks 運用は変更のたびに `/hooks` の trust review が必要
- OSK は SteamVR 全体で同時 1 セッション(`KeyboardAlreadyInUse` 時は TryOpen が false)
- bridge の `serve-mcp` は Codex/Claude Code の `mcp_servers` 登録が必要
- 上流に伝えたいエンジン変更(ControllerRayPointerSource / OverlayKeyboard /
  floatSodaApp.Post)は `patches/` が一次情報。将来方針が変わったときは
  patch 内容をそのまま PR 化できるよう、commit メッセージの粒度を保つ
