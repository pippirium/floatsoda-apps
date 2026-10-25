# ScreenView 実装計画 — 任意のウィンドウをオーバーレイへ配信する

`apps/ChromeView`(現行の Chrome 固定の画面ミラー)を、**任意のウィンドウ・画面を
配信できる汎用アプリ**として改名・拡張する実装計画。上位計画は [PLAN.md](PLAN.md)。

## 0. 対象とゴール

- **改名**: `ChromeView` → `ScreenView`(Chrome 固定の名前・挙動をやめ、どのウィンドウでも配信できる)
- **ウィンドウピッカー**: 現在「右グリップ+スティック長押しで次の候補へ自動巡回」のみの
  対象切替を、**オーバーレイ上の一覧 UI から選べる**ようにする
- エンジン(fork `pippirium/FloatSoda`)への変更は**原則なし**。既存の
  `GestureDetector`( movable オーバーレイ = 左トリガー / ダッシュボード = SteamVR レーザー)、
  `StatefulWidget`、`FloatSodaApp.Post`、スクロール以外のウィジェット群で完結させる

名称の候補は `ScreenView` / `ScreenMirror` / `DesktopCast`。本書は既定を
**`ScreenView`** として書く(実装着手前に確定させる。差し替えは文字列置換のみで済む)。

---

## 1. 改名(Phase 0) — 挙動を変えない機械的リネーム

改名の影響が及ぶ点は次の 2 種類で、**両方とも「オーバーレイキー/アプリキー」由来**:

1. オーバーレイキー = エントリアセンブリ名 + `Title` のスネークケース
   (`WindowKeyGenerator` 規約)。`OverlayManipulator` が `Title` から同一のキーを
   再計算するので、**Title とアセンブリ名を同時に変えれば整合が保たれる**
2. SteamVR vrmanifest の `app_key`。`FloatSodaOptions.AppKey`・SteamVrAutoLaunch で一致させる

### 手順(全て floatsoda-apps リポジトリ内の branch 一本)

| # | 対象 | 変更 |
|---|---|---|
| 1 | ディレクトリ | `apps/ChromeView/` → `apps/ScreenView/`(namespace = ディレクトリ規約) |
| 2 | csproj | `FloatSoda.Samples.ChromeView.csproj` → `FloatSoda.Apps.ScreenView.csproj`。`AssemblyName` を `FloatSoda.Apps.ScreenView` に**明示固定**(キーを固定するため) |
| 3 | namespace | `FloatSoda.Samples.ChromeView` → `FloatSoda.Apps.ScreenView`(全 5 ファイル) |
| 4 | クラス名 | `ChromeViewWidget` → `ScreenViewWidget`(record 名・ファイル名) |
| 5 | ウィンドウ Title | `"Chrome View"` → `"Screen View"`(Program.cs の 3 窓 + `OverlayManipulator` コンストラクタ引数) |
| 6 | AppKey | `floatsoda.sample.chromeview` → `floatsoda.apps.screenview`(`SteamVrAutoLaunch.AppKey`・`FloatSodaOptions`) |
| 7 | vrmanifest | `chromeview.vrmanifest` → `screenview.vrmanifest`(`app_key`・`binary_path_windows` の exe 名・`image_path`・表示名)。`chromeview_icon.png` → `screenview_icon.png`(csproj の None Include も) |
| 8 | 文字列 | コンソール接頭辞 `[ChromeView]` → `[ScreenView]`、デバッグ用テンポラリ `FloatSodaChromeView` → `FloatSodaScreenView`、コメント内の記述 |
| 9 | プロジェクト参照 | `FloatSoda.Apps.slnx` の登録、ルート `README.md`、`PLAN.md` の該当行 |

### 確認事項・注意

- **旧 vrmanifest が SteamVR に登録済みの環境では `floatsoda.sample.chromeview` が残る**。
  SteamVR の「オーバーレイアプリ」設定で手動解除するか、`OpenVR.Applications.RemoveApplication`
  で一度外す手順を README に書く(本体の自動起動連携には影響しない)。
- `WindowCaptureSession` / `WindowLocator` / `OverlayManipulator` はロジック変更なし(Phase 0 では)。
- 検証: `dotnet build` + `--desktop` での起動確認(既知の注記: DesktopWindow の
  ジェスチャは実測していないため、起動と表示のみを確認)。

---

## 2. ウィンドウピッカー(Phase 1) — UI 設計

### 配置と出し方

同一ウィンドウ内に**ピッカーシート**として重ねる(新しいオーバーレイ窓を作らない):

- キャプチャパネルの**上部にヘッダーバー**を 1 行常設:
  `現在の対象(プロセス名 + タイトル, 中点で省略) [一覧]`
- `[一覧]` が押されると、シートがキャプチャ領域を覆う形で開く
- 予約操作: `OverlayManipulator` の対象切替割当(`--switch`、既定「右 グリップ+スティック押し込み 0.3 秒」)を
  **「ピッカーを開閉する」に再定義**する(自動巡回は廃止——一覧が出るため)。巡回を
  保ちたい場合のフォールバックとして `--switch <割当>@cycle` のような記法は作らない
  (シンプルに一本化)。

### 一覧 UI(スクロールなしで実現する)

`ListView` / `GridView` がまだ未公開のため(screen では internal)、**グループ分け + 改ページ**:

- 候補を **プロセス名でグループ化**(アルファベット順)。グループヘッダー行:
  `プロセス名 (N 窗)`
- 1 ページ **7 件**(グループヘッダーも含めて 8 行構成)。下部に `[◀] 2/5 [▶]`。
  ボタンはすべて `GestureDetector`
  — movable 窓なら左トリガー、ダッシュボード窓なら SteamVR レーザーで押せる
- 各行: `タイトル(中点省略) ` + 現在キャプチャ中の対象には `●` マーク
- 行タップ → `WindowCaptureSession.SwitchTarget(hwnd)` → シートを閉じる
- `[再読込]` ボタンで再列挙。列挙は**常時ではなくシートを開いた時点**だけ行う
- 除外対象(Program Manager / TextInputHost / 自プロセス)は Program.cs の
  既存フィルタを共有ヘルパー(`WindowTargets.EnumerateCandidates()`)へ括り出して共用

### データ取得の注意

- `WindowLocator.EnumerateVisibleWindows()` は呼び出しごとにプロセスを開くため、
  ピッカー表示時と再読込時のみ呼ぶ。**バックグラウンドスレッドで実行**し、
  `FloatSodaApp.Post` 経由で `SetState`(メインスレッド STA で Win32 プロセス列挙を
 直接やらない)
- フォントサイズ・行の高さは Dpm(既定 4000)相当で実物視認性を HMD で確認する要がある

---

## 3. 実装手順(Phase 2)

| # | ファイル | 作るもの |
|---|---|---|
| 1 | `WindowTargets.cs`(新) | 候補列挙 + 除外ルールの共有化(`EnumerateCandidates()`) |
| 2 | `WindowPickerPanel.cs`(新) | シート本体。`StatefulWidget`: ページャー・グループ化・現在対象マーク・タップ→切替・閉じるコールバック |
| 3 | `ScreenViewWidget.cs` | 上部ヘッダーバーの追加(対象名 + [一覧]) とピッカー開閉状態の管理。フレーム供給部は触らない |
| 4 | `Program.cs` | `TargetSwitchRequested` の処理を「巡回」から「ピッカー開閉要求」に差し替え(State へ `FloatSodaApp.Post` で通知)。usage コメントの更新 |
| 5 | `OverlayManipulator.cs` | ロジック変更なし(イベント名は `TargetSwitchRequested` のまま、意味の doc を更新) |

落とし込み方針(実装方針):

- ピッカーとキャプチャの状態は分離。`WindowCaptureSession` は 1 ウィンドウ 1 インスタンスの
  現行仕様のまま、**切替だけ呼ぶ**(プール再作成による最初の数フレームの空白は許容——切替時点で
  シートで覆われており、視覚的ノイズも軽減される)。切替後はシートを閉じて新対象を表示する
- 縦横比が特殊な窓(極端に細長い・小さい)もそのまま表示(`BoxFit.Contain` 維持)。
  配置やサイズの自動調整は既存の `OverlayManipulator`(掴み移動・スティック拡縮)で行う

---

## 4. 検証(Phase 3)

- `dotnet build`(0 エラー)+ `--save-frames`(capture 経路の回帰確認)
- `--desktop`: 起動・シートの描画確認(ジェスチャ未検証のため、押下は要 HMD)
- **実機(HMD、所有者)**:
  - ワールド窓で [一覧] → 左トリガーでの行選択 → 切り替わること
  - 右の「グリップ+スティック押し込み長押し」でシートが開くこと
  - 切替の遅延(0.3〜1 秒目安)と、切替直後の最初の数フレームの空白の見え方
  - SteamVR 自動起動を ON にして、改名後の `app_key` で IdentifyApplication が通ること
- 併用: WristAgent と同時に起動して干渉(OSK 同時1セッション・ホットキー)がないこと

## 5. 引き継ぎ・将来

- `WristAgent` の名称も `FloatSoda.Apps.WristAgent` へ統一するかどうか(改名で
  オーバーレイキーが変わるため、振動/OSK のキー依存の確認が要る)。本計画では触らない
- `ListView` がスクロール基盤ごと公開されたら、ピッカーを改ページからスクロール一覧へ作り替える
- build-only CI の追加(任意)
