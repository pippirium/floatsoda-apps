# floatsoda-apps

SteamVR オーバーレイの実用アプリ集。フレームワーク(FloatSoda)は
[`pippirium/FloatSoda`](https://github.com/pippirium/FloatSoda) fork を
`engine/` submodule として pin して使います。設計は [PLAN.md](PLAN.md) を参照してください。

## 構成

| ディレクトリ | 内容 |
|---|---|
| `engine/` | git submodule → `pippirium/FloatSoda` の main を pin。フレームワーク変更は fork 側で行い、ここでは pin の更新だけを行う |
| `apps/WristAgent/` | 左腕の腕時計位置に AI エージェントの承認要求カードを出すアプリ(`apps/WristAgent/README.md` 参照) |
| `apps/ScreenView/` | 任意のウィンドウ(デスクトップ画面)を VR オーバーレイに表示するアプリ |

## ScreenView のキャプチャ対象の選び方

起動時の対象は `--process` / `--window` / `--pid` で指定しますが、起動後も
オーバーレイ上のピッカーから切り替えられます。

- オーバーレイ上部の `[一覧]` を押すとシートが開く(movable 窓は左トリガー、
  ダッシュボード窓は SteamVR レーザー)
- ワールド窓では「右 グリップ+スティック押し込み 0.3 秒長押し」でも開閉できる
  (`--switch <割当>` で変更可。旧仕様の「候補を自動巡回」は廃止しました)
- シートはプロセス名でグループ化した一覧を 1 ページ 8 行で表示し、`[◀][▶]` でページを、
  `[再読込]` で候補の再列挙、`[閉じる]` でシートを閉じられます。行を押すとその窓へ切り替わり
  現在キャプチャ中の窓には `●` が付きます

## ScreenView の VR パネル操作とサイズ追従

ワールド表示のパネルは、左右グリップで掴んで移動し、**掴んでいないときの右スティック上下**で
表示サイズを変更します(上で拡大、下で縮小。幅は 0.15m〜4m)。スティック操作は
**物理的な表示幅のみ**を変更します。高さはキャプチャ画像の縦横比に追従するため、
縦長や横長のウィンドウでも映像は引き伸ばされません。

キャプチャ先のウィンドウをリサイズしたり、ピッカーで別のウィンドウに切り替えたりすると、
オーバーレイの縦横比は新しいキャプチャ画像に自動追従します。
**一度でも右スティックで調整した後は、そのとき指定したVR空間上のパネル幅を維持**し、
新しい縦横比に合わせて高さだけが変わります。未操作の間はエンジンの通常の初期サイズ計算を使います。
これは手動拡縮と自動追従の競合を解消するための仕様です。

SteamVR/HMD で確認する場合は、(1) 右スティックで幅を変更、(2) 対象ウィンドウを
リサイズ、(3) ピッカーで縦横比が異なる別ウィンドウへ切替、(4) 幅が維持されて
高さだけが比率に合わせて変わること、(5) さらに右スティックで拡縮できることを確認してください。

## 起動(SteamVR 起動中)

WristAgentは「左スティックを中央のまま押し込み + 右スティック上下」で、Codexのチャットだけを
スクロールできます（グリップを離して操作）。ScreenViewが別アプリを映していても対象はCodexです。
この間はScreenViewのパネルの拡大縮小を行いません。
WristAgentの音声入力・停止・送信にもレーザー不要の組み合わせがあります（`apps/WristAgent/README.md`）。

```powershell
dotnet run --project apps/WristAgent
dotnet run --project apps/ScreenView
```

### 旧 `ChromeView` の登録を解除するには

`apps/ChromeView` は `apps/ScreenView` へ改名し、SteamVR の `app_key` も
`floatsoda.sample.chromeview` → `floatsoda.apps.screenview` に変更しました。
改名前に自動起動を ON にしていた環境では、旧キー `floatsoda.sample.chromeview` の登録が
SteamVR の「オーバーレイアプリ」設定に残ります。設定画面から手動で解除してください
(解除しなくても新キーでの自動起動には影響しません)。

## エンジンを更新するとき

フレームワークの変更は fork(`pippirium/FloatSoda`)内の branch → fork main merge で行い、
このリポジトリでは pin の更新だけを行います。詳細な手順と禁止事項(上流への変更依頼など)は
[PLAN.md](PLAN.md) の「エンジン側の運用」の節にあります。

```bash
cd engine
git fetch
git checkout main && git pull
git push origin main        # fork へ送信
cd ..
git add engine && git commit -m "chore(engine): submodule を <commit> へ更新"
```
