# ScreenView 残タスク実装計画

[PLAN-ScreenView.md](PLAN-ScreenView.md) の Phase 1 で確定した挙動を前提に、レビュー後に残った
タスクを別 PR に分けて進めるための計画。Phase 1 自体の設計・実測で確定したレイアウト制約は
[PLAN-ScreenView.md](PLAN-ScreenView.md) の側にある。

スコープ外として引き継ぐ項目（本書では実装しない）:

- **`--desktop` が HMD 無しで起動しない**: `FloatSodaApp.Initialize()` の OpenVR 初期化が
  窓の種別より先に走るため `Init_HmdNotFound` で落ちる。修正は engine 側の設計判断
  （`engine/AGENTS.md` の方針どおり本リポジトリでは触らない）。検証は
  `WidgetBitmapRenderer` + ピクセルサンプリングによるヘッドレス描画が事実上の代替手段になっている
- **実機での視認性**（ワールド窓 Dpm 既定 / ダッシュボード窓 `Dpm(1600)`）と操作確認:
  PR #2 の「検証（未実施と理由）」に記載したとおり未実施

---

## タスク A: `--process` の既定値を `chrome` から変更する ✅ 適用済み

**PR**: `default-to-frontmost-window`

### 変更前

```csharp
var processFilter = args.Contains("--process") ? GetOptionValueAfter("--process") : "chrome";
```

起動時に `WindowLocator.Pick(…)` へ渡すプロセス名フィルタの既定が `chrome`。ScreenView は
「任意のウィンドウを配信する」アプリなので特定プロセス既定は趣旨と食い違う。
`--list-windows` の ` * ` マーカーもこの既定に連動していた。

### 適用した内容

**既定を「前面のウィンドウ（フォアグラウンド）」に変更**した。

- `WindowLocator.GetForegroundHandle()` を追加（`PInvoke.GetForegroundWindow`。
  `NativeMethods.txt` へ `GetForegroundWindow` を追記）
- `Program.cs` は `--process` / `--window` / `--pid` のいずれも未指定のとき、前面ウィンドウを
  初期対象にする。前面が候補リストに居ない（除外対象＝シェル由来、またはタイトル無し）場合は
  候補の先頭へフォールバックし、それも無いときだけ従来どおりエラーにする
- `--process <名前>` は従来どおりプロセス名指定で選べる（既定値は撤廃）
- usage コメントは「既定は前面のウィンドウ」、`--list-windows` の ` * ` は**前面の窓**を示す
- 前面ウィンドウが SteamVR オーバーレイ（自プロセス以外の上記除外）に混ざることはないが、
  候補列挙側の除外ルール（`explorer` / `TextInputHost` / `SearchHost` /
  `StartMenuExperienceHost`）を前面選択にも適用する

### 却下した案と理由

| 案 | 却下理由 |
|---|---|
| 既定を空文字にして「見つからなければエラー」 | 引数なし起動（SteamVR 自動起動時）で毎回失敗して使えない |
| 既定でピッカーを開く | 自動起動時にシートが画面を覆い、配信が見えない。操作できない状態になる |
| `--process` を必須にする | 自動起動の vrmanifest 起動は引数を持てないため不可 |

### breaking change の扱い

既定動作が変わる（起動時の対象が chrome 固定 → 前面窓）。個人用 fork のアプリで、
既存の外部参照は README の起動手順のみなので受け入れる。PR 本文に「breaking（個人利用範囲で
受け入れる）」として明記する。

---

## タスク B: `PLAN.md` の上流リポジトリ表記を修正する ✅ 適用済み

**PR**: `docs-plan-followups`（docs-only）

### 変更前の誤り（2 箇所）

| 位置 | 記述 | 問題 |
|---|---|---|
| `PLAN.md:18` | 上流 `sumx21t/FloatSoda` に対する PR / Issue / あらゆる変更依頼（禁止事項） | 実在しないアカウント名。正しい上流は `sumx21t-3310/FloatSoda` |
| `PLAN.md:91` | `git remote add upstream https://github.com/sumx21t/FloatSoda.git` | 同上。この手順どおりに打つと remote が解決できない |

**危険性の観点**: 禁止事項の行が別名指しになっていたため、「`sumx21t-3310/FloatSoda` は
禁止対象外のようだ」と誤読される余地があった。fork 境界の安全装置として働く文章なので、
docs-only としては優先度が高い。

**上流の実在確認（2026-10-26）**: `gh api repos/sumx21t-3310/FloatSoda` は 200、
`gh api repos/sumx21t/FloatSoda` は 404。`pippirium/FloatSoda` の parent も
`sumx21t-3310/FloatSoda`（読み取りのみで、書き込みはしていない）。

### 適用した内容

- 2 箇所を `sumx21t-3310/FloatSoda` に修正
- 上流 remote の手順ブロックに「**読み取り専用**で使う（`git push upstream …` は禁止事項に該当）」を
  1 行追記。コマンド例自体が送信と誤読されうるため
- `git grep -n "sumx21t/FloatSoda"` が 0 件になることを確認（ハイフン付き以外ゼロ）
- `AGENTS.md` の GitHub write boundary と矛盾しないことを確認

---

## タスク C: `[再読込]` の列挙結果の扱いを直す ⏳ 実装中（未検証）

**PR**: `picker-reload-feedback`

### 変更前の問題（コードから確定した事実）

1. **失敗が UI に届かなかった。** `Program.cs` の `RefreshCandidates()` は列挙例外を
   `Console.Error.WriteLine` して `return` するだけで `OfferCandidates` を呼ばない。
   State は `_candidateMessage = "候補を列挙しています…"` を設定したまま戻さないため、
   シートが**永久に「候補を列挙しています…」で固まっていた**
2. **0 件と失敗が区別できなかった**（0 件と同じ表示になり、再読込で直せるか判断できない）
3. **連打で応答が競合した。** 多重 `Task.Run` の古い応答が新しい応答を後から上書きした
   （`OfferCandidates` に世代管理が無かった）
4. **送信中の進捗表示と連打抑止が無かった**
5. **ビルドのたびにピッカー全体が作り直されていた。** `Build()` が
   `OnReload = RefreshCandidates` / `OnClose = () => SetState(…)` / `OnPick = PickWindow` と
   新しい委任インスタンスを作っていたため、`Element.UpdateChild` の参照平等ショートパス
   （`engine/src/FloatSoda/Elements/Element.cs:202`）が効かず、ピッカーの部分ツリーと
   `GestureDetector` が毎回再構築されていた

### 実装した内容（未検証・PR に上げる前に実測を確定させる）

- **C-1 結果を型づけ**: `CandidateResult(Candidates, Error)` を追加し、
  `ScreenViewWidget.OfferCandidates` / `RequestCandidateRefresh` をこれに統一。
  失敗時も必ず State へ返す（`Program.cs` で例外を `CandidateResult.Failed(…)` に変換して `Post`）
- **C-2 世代カウンタ**: State の `_refreshGeneration` を要求のたびインクリメントし、
  応答の世代が現在値と違えば破棄。最後に要求した結果だけが画面に残る
- **C-3 状態表示の分離**: 送信中 / 失敗 / 候補なし / 候補あり の 4 態にし、
  失敗は「候補の列挙に失敗しました: …」を別の文言として出す
- **C-4 連打抑止**: 送信中は `[再読込]` を `BuildButton(disabled: true)` で無効化
- **C-6 エラー時に候補を差し替えない**: エラー時は `_candidates` を据え置く。空配列を渡すと
  `WindowPickerPanel` のページ寄せ（編集中の窓がいるページへ `_page` を寄せる処理）が
  「候補が消えた」と誤判定して 0 件ページに切り替えるため
- **C-7 ページ制御**: 候補 0 件・送信中は `[◀]` `[▶]` を disabled、`n/N` は「–」
- **C-8 委任の固定**: 要求口（`RequestCandidateRefresh` / `ClosePickerRequested` /
  `PickWindowRequested`）を `BindHooks()` でウィジェットへ一度だけ結び、`Build()` はその
  参照をそのまま渡す。ビルドごとの委任作り直しをやめ、ピッカーの部分ツリーが差分で止まる
- **C-9 応答の反映を構築時に寄せる**: `OfferCandidates` は結果を `_pending` に控えるだけにし、
  反映は `Build()` の先頭（`ApplyPendingCandidates()`）で行う。`SetState` は予約しかしないため、
  呼ばれた時点で反映すると後から届いた要求の世代判定が壊れる

### 検証の状況（2026-10-26：まだ終わっていない）

ヘッドレスでシート操作を再現する使い捨ての足場（`MultiFrameRenderer`）で 9 項目を確認し、
**7 項目 OK / 2 項目 NG** の状態。NG は「応答が返らない間は一覧部が送信中のまま」と
「応答の到着順が入れ替わっても最後に要求した結果が残る」で、どちらも
**応答が来る前の中間状態を観察できていない**症状になっている。

原因の切り分けで確定した engine の挙動（すべてソースと実行で確認した事実）:

- `StatefulElement.Update(newWidget)` は `base.Update` ののち `State.DidUpdateWidget(oldWidget)` を
  呼ぶ（`engine/src/FloatSoda/Elements/ComponentElement.cs:141-150`）。そのためルート ウィジェットを
  新しいインスタンスへ再接続するたびに、要求口が `UnbindHooks` → `BindHooks` で結び直される
- `Element.UpdateChild` は `ReferenceEquals(child.Widget, newWidget)` のときだけ更新を省く
  （`Element.cs:200-206`）。値が等しい別インスタンスは `Update` へ流れる
- `RenderObjectToWidgetAdapter.AttachToRenderTree(owner, element)` は再接続で `NewWidget` を設定して
  `MarkNeedsBuild` するだけで、構築は呼ばれた側の `BuildOwner.BuildScope()` が行う
  （`RenderObjectToWidgetAdapter.cs:48-66`）
- `State.SetState` は `Element.MarkNeedsBuild()` で予約するだけ。実際の反映は
  `WidgetBinding.DrawFrame` の `BuildOwner.BuildScope()`（`WidgetBinding.cs:177-183`）

**ヘッドレス検証の制約**（先に本書に書いていた記述は誤りだったので訂正する）:
`WidgetBitmapRenderer` / `LayerBitmapRenderer` でのビットマップ生成は動くが、この検証環境では
**ヘッダーバーより下がすべて `#000000` に塗られ**、ピクセルサンプリングではシートの一覧部を
確認できない（`DumpTree()` は `RenderConstrainedOverflowBox 1920x1080` → `RenderStack 1920x1080` →
`RenderPositionedBox 1920x1080` → `RenderParagraph 567x40` と正しくレイアウトされている）。
よってタスク C の表示検証は**ピクセルではなく State の値の読み出し**で行う。

### この検証で判明した設計上の注意点

- 口（`TogglePickerRequested` など）が結ばれるのは `InitState` / `DidUpdateWidget` のときだけ。
  初回接続のマウントでは `DidUpdateWidget` が走らないため、検証はマウント後にもう 1 度
  再接続して口を結ばせる必要があった（実際のアプリは `Program.cs` がマウント前に結ぶので無関係）
- 検証用に State へ `public` を増やさない方針。読み出しは `ScreenViewState` の
  `CurrentCandidates` / `CurrentCandidateMessage` / `IsCandidateRefreshing` の 3 つに限定し、
  操作は実際の UI と同じ口（`TogglePickerRequested` / `RequestCandidateRefresh` /
  `OfferCandidates`）から行う

### 見送った項目

- **C-5 列挙コストの削減**（`Process.GetProcessById` の多用を `Process.GetProcesses()` 全取得 +
  PID ディクショナリへ集約）: `Process.GetProcesses()` も全プロセスを開くため効果が不確か。
  **実機で重さが体感できてから**判断する（推測で触らない）
- **終了した窓の表示**（`ActiveHandle` が候補に居ないとき `●` が消える）: 対象窓が閉じたら
  配信自体が止まるため、ピッカーより先にキャプチャ側の状態表示として扱うべき課題

---

## PR の切り方（実績）

| 順 | ブランチ | 内容 |
|---|---|---|
| 1 | `docs-plan-followups` | タスク B（`PLAN.md:18` / `:91`）+ 本書 + `PLAN-ScreenView.md` §5 の追記 |
| 2 | `default-to-frontmost-window` | タスク A |
| 3 | `picker-reload-feedback` | タスク C |

PR は**積み上げ（stack）にしない**。3 本とも `pippirium/floatsoda-apps` の `main` を base とする
（ブランチ自体は作業の都合上 1 → 2 → 3 の順に切っているが、内容の重なりはタスク B の docs と
タスク A のコードが別ファイルというだけなので、どの順でマージしても衝突しない）。
Issue は作らず本文で代替理由を明記、コミットは
`pippingo002 <49540108+pippirium@users.noreply.github.com>` + `Co-authored-by`、
ブランチ名にツール名を入れない。上流 `sumx21t-3310/FloatSoda` への書き込み・同期は行わない。

## 実機確認との関係

タスク A は前面ウィンドウ選択を伴うため、実機が復旧してから挙動確認するのが安全
（HMD 無しでは `--desktop` も OpenVR 初期化で落ちるため、`--save-frames` でしか確認できない）。
タスク B は実機不要。タスク C は上記のとおりヘッドレスのピクセル検証が使えないため、
State の値で確認する（実機での表示確認は未実施のまま）。
