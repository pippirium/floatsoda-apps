# SteamVR Overlay 起動手順書

作成日: 2026-11-01  
対象: `floatsoda-apps` の ScreenView と WristAgent。Windows x64 上の PowerShell で操作します。以下の `C:\src\floatsoda-apps` は実際のチェックアウト先に置き換えてください。

## 1. 起動前の準備

1. Steam を開き、SteamVR を起動します。
2. HMD と左右のコントローラーを接続し、SteamVR で認識されていることを確認します。
3. HMD を装着し、SteamVR ダッシュボードを開けることを確認します。
4. ScreenView に映すアプリ（例: Chrome）を開き、対象ウィンドウを表示しておきます。

既に同じ Overlay が起動している場合は、終了してから起動してください。自動起動で動いている場合もあります。

## 2. 初回・ソース更新後のビルド

PowerShell を開いて実行します。.NET 10 SDK が必要です。

```powershell
Set-Location 'C:\src\floatsoda-apps'
dotnet --list-sdks
dotnet build FloatSoda.Apps.slnx --configuration Release
```

SDK 一覧に `10.0.xxx` があり、ビルドのエラーが 0 件であることを確認します。ビルドが失敗した場合は、以降へ進まず出力を確認してください。

`engine/src` が存在しない新規チェックアウトの場合のみ、ビルド前に次を実行します。既存環境の engine ブランチや pin を起動のために切り替える必要はありません。

```powershell
git submodule update --init --recursive
```

## 3. ScreenView を起動する（アプリの画面を VR に表示）

### 通常の起動

Chrome を開いた状態で、次を実行します。ビルド済みなら、毎回ビルドする必要はありません。

```powershell
Set-Location 'C:\src\floatsoda-apps'
& '.\apps\ScreenView\bin\Release\net10.0-windows10.0.19041.0\FloatSoda.Apps.ScreenView.exe' --process chrome
```

HMD 内のワールド空間に `Screen View` パネルが出て、Chrome の画面が更新されれば起動成功です。初期位置はプレイエリア原点を基準に `(0, 1, -1)` m です。現在の顔の向きに追従する配置ではないので、見つからない場合は周囲を確認します。

### Chrome 以外を表示する

まず候補を列挙します。このコマンドは一覧を出して終了します。

```powershell
& '.\apps\ScreenView\bin\Release\net10.0-windows10.0.19041.0\FloatSoda.Apps.ScreenView.exe' --list-windows
```

表示された `[プロセス名]` を `--process` に指定します。例えば Microsoft Edge の場合:

```powershell
& '.\apps\ScreenView\bin\Release\net10.0-windows10.0.19041.0\FloatSoda.Apps.ScreenView.exe' --process msedge
```

同じプロセスに複数の窓がある場合は `--window 'タイトルの一部'` または `--pid 12345` を追加します。`12345` は一覧の実際の PID に置き換えてください。**`--window` と `--pid` は `--process` の条件と併用されます。省略するとプロセス条件は `chrome` のままです。**

### 起動後の操作

| 操作 | 動作 |
|---|---|
| パネル上部の `[一覧]` を左トリガーで選ぶ | キャプチャ対象の一覧を開く |
| 一覧の行を選ぶ | 表示するウィンドウを切り替える |
| `[◀]` / `[▶]` / `[再読込]` / `[閉じる]` | ページ移動 / 候補更新 / 一覧を閉じる |
| 左右いずれかのグリップ | パネルを掴んで移動する |
| 掴んでいない状態で右スティック上下 | パネルを拡大・縮小する |
| 左グリップ＋左スティック押し込みを 0.7 秒長押し | パネルの表示・非表示を切り替える |
| 右グリップ＋右スティック押し込みを 0.3 秒長押し | 対象一覧を開閉する |

### 表示モードの注意

- **HMD 内の通常表示は追加フラグなし**で起動します。
- `--desktop` は PC モニター上で確認するモードです。HMD 内への Overlay 表示には使いません。
- `--dashboard` は SteamVR ダッシュボード用です。アプリのマニフェスト登録が必要なので、初回は通常表示で起動してください。
- **現実装の `--world` は `--dashboard` と同じ分岐に入ります。** ワールド空間に表示したいときは `--world` を付けないでください。

## 4. WristAgent を起動する（左手首にカードを表示）

別の PowerShell を開いて実行します。ScreenView と併用できます。

```powershell
Set-Location 'C:\src\floatsoda-apps'
& '.\apps\WristAgent\bin\Release\net10.0\FloatSoda.Samples.WristAgent.exe'
```

左コントローラー付近の腕時計位置に `WristAgent` カードが表示されれば起動成功です。左コントローラーの追跡が有効であることも確認してください。

既定の HTTP ポートは `8797` です。競合する場合は末尾に別ポートを指定できます。

```powershell
& '.\apps\WristAgent\bin\Release\net10.0\FloatSoda.Samples.WristAgent.exe' 8801
```

カード表示の確認後、AI エージェントから要求を送るには WristAgentBridge などの接続設定が別途必要です。[WristAgent の README](../apps/WristAgent/README.md) を参照してください。ポートを変更した場合は接続側も合わせます。

## 5. 終了する

各アプリを実行している PowerShell で `Ctrl+C` を押します。HMD 内から対象の Overlay が消えたことを確認します。

プロセスが残っているかは、次で確認できます。

```powershell
Get-Process -Name FloatSoda.Apps.ScreenView,FloatSoda.Samples.WristAgent -ErrorAction SilentlyContinue |
    Select-Object Name, Id
```

応答せず残った場合に限り、表示された対象アプリの PID を指定して終了します。次の `12345` は実際の PID に置き換えてください。

```powershell
Stop-Process -Id 12345
```

SteamVR の `vrserver` や `vrcompositor` を強制終了すると、VRChat など実行中の VR アプリにも影響します。Overlay の終了には対象アプリだけを終了します。

## 6. ScreenView の自動起動

ScreenView は通常の VR 起動時に、同梱の `screenview.vrmanifest` を使って SteamVR への登録と自動起動の有効化を試みます。コンソールの「SteamVR 自動起動…」の出力で結果を確認できます。失敗しても Overlay の実行は継続します。

- 次回の SteamVR 起動時に自動起動する場合があるため、手動起動前に重複していないか確認します。
- マニフェストの起動引数は空なので、自動起動時のキャプチャ対象は既定の `chrome` です。手動実行で指定した `--process` などは保存されません。Chrome を先に開くか、上記の手動起動を使ってください。
- 自動起動を止める場合は、ScreenView を終了してから SteamVR 設定のオーバーレイアプリの自動起動項目で `FloatSoda Screen View` を無効にします。設定項目名は SteamVR のバージョンで異なる場合があります。
- 現実装は次の VR 手動起動でも有効化を試みるため、無効化は再度必要になることがあります。
- 登録後に `.exe` やマニフェストを移動すると、自動起動先と一致しなくなる場合があります。

## 7. 表示されないとき

| 症状 | 確認・対処 |
|---|---|
| `.exe` が見つからない | 作業場所とビルド結果を確認し、手順 2 の Release ビルドを実行する |
| 「キャプチャ対象のウィンドウが見つかりませんでした」 | 対象の窓を開き、`--list-windows` のプロセス名とタイトルを確認する。`--process` の指定漏れにも注意する |
| PC 上には出るが HMD 内に出ない | `--desktop` を付けていないか確認する |
| ScreenView の位置が分からない | プレイエリア原点付近を確認し、非表示切替の操作も試す |
| WristAgent が見つからない | 左コントローラーの接続・追跡と、腕時計位置を確認する |
| WristAgent の起動時にポート競合する | 既存プロセスを確認する。別用途でポート使用中なら手順 4 のポート変更を使う |
| HMD 検出エラーが出る | SteamVR 自体で HMD とダッシュボードが使えるか確認する。改善しなければ、実行中の VR アプリを終了してから SteamVR を通常操作で再起動する |
| 自動起動だけ失敗する | コンソールの登録エラーと、`.exe` の隣に `screenview.vrmanifest` があるかを確認する |

必要なら、SteamVR のプロセスの有無を読み取り専用で確認できます。ただし、プロセスが存在するだけでは HMD 内への表示成功とは判定できません。

```powershell
Get-Process -Name vrserver,vrmonitor,vrcompositor -ErrorAction SilentlyContinue |
    Select-Object Name, Id, StartTime
```

## 確認元・検証範囲

本書はローカルの [ScreenView 実装](../apps/ScreenView/Program.cs)、[対象検索](../apps/ScreenView/WindowLocator.cs)、[自動起動処理](../apps/ScreenView/SteamVrAutoLaunch.cs)、[マニフェスト](../apps/ScreenView/screenview.vrmanifest)、[WristAgent 実装](../apps/WristAgent/Program.cs) に基づきます。

作成時には出力先パスの定義・参照リンクと実装の対応を確認しました。アプリ起動、SteamVR 設定変更、HMD 内での表示確認は行っていません。
