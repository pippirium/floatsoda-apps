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

## 起動(SteamVR 起動中)

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
