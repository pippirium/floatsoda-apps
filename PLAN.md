# floatsoda-apps 移植計画

ChromeView(Chrome / デスクトップ画面の VR オーバーレイ共有・現 `apps/ScreenView`)と WristAgent(腕への
エージェント承認 UI)を、FloatSoda フレームワークから独立したアプリリポジトリとして
切り出すための計画。

## 1. 目的と位置づけ

| レイヤ | 場所 | 変更の流れ |
|---|---|---|
| フレームワーク(エンジン部分) | fork `pippirium/FloatSoda`(submodule で pin) | **fork で branch を切って作業 → fork main へ merge(必要なら fork 内 PR)→ 本リポジトリは submodule の pin 更新で取り込む** |
| アプリ機能部分 | 本リポジトリ(WristAgent / ScreenView / bridge tool) | ここで自由に開発。必要なエンジン変更は fork 側で作り、その結果を submodule で消費する |

**前提(位置づけ)**: fork は **上流に取り込まれる想定のない、個人用カスタマイズ領域** です。

**禁止事項(運用中に絶対に行わない)**:

- 上流 `sumx21t-3310/FloatSoda` に対する **PR / Issue / あらゆる変更依頼**。
  (上流リポジトリの URL 空間を対象にした送信操作は一切しない)

**許可される操作(すべて fork 内でのみ)**:

- fork 内で branch を切り、**fork の main を base にした PR → merge** も自由に可
  (base が fork main である限り GitHub は上流へ何も通知しない)
- ローカル merge + push も可。好みで選んでよい
- 上流の新しいコミットを取り込むのも、fork main への merge / fork 内 PR 何れでも可

⇔ 境界は「**上流向け送信はゼロ、fork 内は全自由**」の1本だけ。

## 2. 全体構成(目標形)

```text
floatsoda-apps/
├── PLAN.md                    ← 本書
├── README.md                  ← アプリ全体の説明・起動方法・エージェント接続手順
├── .gitignore                 ← .NET 標準(floatsoda リポジトリから流用)
├── engine/                    ← git submodule → fork(pippirium/FloatSoda main を pin)
│   └── (upstream 全体: src/ tests/ docs/ samples/)
├── apps/
│   ├── WristAgent/            ← FloatSoda.Samples.WristAgent から移植
│   └── ScreenView/            ← FloatSoda.Samples.ChromeView から移植 → ScreenView へ改名(PLAN-ScreenView.md)
│       (OverlayManipulator を含む)
└── tools/
    └── WristAgentBridge/      ← hooks 中継 + stdio MCP サーバー
```

- `engine/` は fork リポジトリ全体をサブモジュール化する(src/ だけの部分木分割は
  `git subtree split` の維持コストが高いため)。tests/ や docs/ も入るが無害。
- `apps/` と `tools/` はソリューション(仮称 `FloatSoda.Apps.slnx`)に登録し、
  `csproj` の `ProjectReference` は `../../engine/src/FloatSoda/...` を指す。

さらに、アプリ側からこの submodule はビルドに必要なだけを消費する
(src/FloatSoda / .Engine / .OVR / .Rendering / .Abstractions + tests)。
fork から想定外の範囲を持ってくるとしても、PR が飛ぶ要素は存在しない

## 3. エンジン取り込み方式の決定

| 方式 | 上流取り込み | IDE/ビルド | 採否 |
|---|---|---|---|
| **submodule → fork main**(採用) | fork 上の branch merge でエンジン更新を作り、pin 更新 | 正常動作。Rider は submodule も1窓で見える | ✅ fork 内で完結。上流への送信ゼロ |
| submodule + patch 系列 | 上流 pin + 本リポジトリ内 patch | 同 | ❌ patch 運用の手間が継続する。捨てた |
| subtree | `git subtree pull --squash` で全量マージ、競合抵抗が継続 | 同 | ❌(ノイズ大) |
| NuGet パッケージ参照 | バージョン更新のみ。**未リリースのエンジン修正(ControllerRayPointerSource など)が使えない** | 同 | ❌(現段階) |

### エンジン側の運用(更新のたび)

```bash
# 1) fork で branch を切ってエンジン変更を作る
cd engine           # submodule 内は fork の checkout
git checkout -b <slug>
# ...実装 + tests + docs 更新(floatsoda リポジトリのコミット規約に従い、メッセージは日本語)...
git add -A && git commit

# 2) fork main へ merge。fork 内 PR(base=main)経由でも、ローカル merge でもよい
#    - PR を使う場合: github 上で pippirium/FloatSoda の main を base に PR → merge
#      (本リポジトリから gh pr create --base main --head <slug> でも可)
git checkout main
git merge --no-ff <slug>          # 競合時は自分で解消
git push origin main              # fork 上の main を更新(上流に通知は来ない)

# 3) 本リポジトリで pin を更新
cd ../..
git add engine && git commit -m "chore(engine): submodule を <commit> へ更新"
```

上流の新しいコミットを取り込む場合も fork main 上で直接 merge する
(同じく上流への送信は発生しない):

```bash
cd engine
git remote add upstream https://github.com/sumx21t-3310/FloatSoda.git   # 初回のみ(read-only)
git checkout main && git fetch upstream
git merge upstream/main      # 競合時は fork 側(自前差分)を優先して解消
git push origin main
```

**注意**: 上流 remote は**読み取り専用**で使う。`git push upstream …` に相当する操作は
上の禁止事項に該当するため行わない(取り込みは `git fetch upstream` + fork 側の merge のみ)。

**注意**: submodule 内で merge/push を行うと、submodule HEAD が先に進み、
本リポジトリ側の pin が古くなる。エンジン更新が完了したら必ずステップ 3) の
pin 更新を本リポジトリでコミットする(忘れると本リポジトリのビルドが旧寸法のまま)

## 4. 移植手順

### Phase A — fork main の整備

1. fork(`pippirium/FloatSoda`)の `main` に、実装済みブランチを merge する
   (fork 内 PR での merge でもローカル merge でも可)
   - `182-device-tracked-pointer`(ControllerRayPointerSource / OverlayWindow 一般化 /
     OverlayKeyboard / FloatSodaApp.Post / docs 更新 + WristAgent サンプル・bridge tool)
   - `chrome-view-sample`(WGC 画面キャプチャ)
   - 競合想定なし(着手ファイルが別)だが、merge 後に `dotnet build` + 全テストを実行
   - 先に fork main に `upstream` リモートを設定し、main を上流 HEAD まで上げておく
     (merge は fork 内で完結。fork 内 PR にしてもよい)
   - merge は `git merge --no-ff` とし、merge commit を残す(後から差分の由来が復元できる)
2. merge 結果を fork に push する(上流へは何も送らない)
3. submodule 登録:
   ```bash
   git submodule add https://github.com/pippirium/FloatSoda.git engine
   cd engine && git checkout <merge済みの main HEAD> && cd ..
   git commit -m "chore(engine): FloatSoda(fork) を pin"
   ```

### Phase B — アプリ部分の移植

4. `apps/WristAgent/`:
   - `engine/samples/FloatSoda.Samples.WristAgent/` から csproj・ソース一式をコピー
   - **まずはコード改変なし**で動かす(移植差異を最小化するため)。動作確認後に改名を検討
   - ProjectReference を `../../engine/src/FloatSoda/FloatSoda.csproj` へ修正
   - slnx 登録
5. `apps/ScreenView/`:
   - `engine/samples/FloatSoda.Samples.ChromeView/`(Program.cs + `OverlayManipulator.cs`)を移植
   - エンジン変更が要ることが判明したら、**fork 側で branch → commit → main merge** し、
     pin をあげてから使い続ける
   - こちらも基本的にコード改変なし
6. `tools/WristAgentBridge/`: そのままコピー(外部依存なし)
7. README・README 内のパス・設定例(`dotnet run --project ...` のパス)を新構成へ書き換え

### Phase C — 動作確認と整地

8. `dotnet build` — submodule の参照解決を確認
9. 実機確認(SteamVR 起動中、HMD 着用):
   - ChromeView(現 ScreenView): 画面がオーバーレイに出る
   - WristAgent: 腕にカード・レーザー選択・OSK 入力・振動・ホットキー
   - 併用: ScreenView を見ながら腕で承認要求を返せるか
10. 移植後、fork 側の `samples/FloatSoda.Samples.WristAgent` と
    `tools/WristAgentBridge` を(fork 上のブランチでの削除コミット + main merge で)
    削除して、アプリ部分は本リポジトリが正とする(重複が残るとどちらが正か曖昧になる)。
    フレームワーク変更は fork 上に残し続ける
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
- docs(Input.md など)は engine 側(fork の FloatSoda)に属する。本リポジトリには
  アプリ固有の README・エンドユーザー向け接続手順のみ置く。
- submodule 内での作業は **commit / merge / push / fork 内 PR すべて許可**
  (fork は個人用カスタマイズ領域)。ただし **上流リポジトリへの PR / Issue /
  push など上流対象の送信操作はしない**。

## 6. 引き継ぎ事項(移植中も閉じない)

- 実機検証未実施: UV→ピクセルの Y 反転の仮定(反転なし)ほか、
  レーザー選択/OSK/振動/ホットキーの実測(本リポジトリの apps/ 上で確認)
- 音声入力(yes/no を声で答える)は計画のみ。本リポジトリ側の拡張として引き継ぐ
- hooks 運用は変更のたびに `/hooks` の trust review が必要
- OSK は SteamVR 全体で同時 1 セッション(`KeyboardAlreadyInUse` 時は TryOpen が false)
- bridge の `serve-mcp` は Codex/Claude Code の `mcp_servers` 登録が必要
- fork 上に残すエンジン変更(`ControllerRayPointerSource` / `OverlayKeyboard` /
  `FloatSodaApp.Post` / `OverlayWindow` 一般化)は、将来のエンジン依存整理
  (たとえば NuGet 化)が済むまで fork main の常駐差分として維持する
