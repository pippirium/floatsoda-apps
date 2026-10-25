# floatsoda-apps

SteamVR オーバーレイの実用アプリ集。フレームワーク(FloatSoda)は
[`pippirium/FloatSoda`](https://github.com/pippirium/FloatSoda) fork を
`engine/` submodule として pin して使います。設計は [PLAN.md](PLAN.md) を参照してください。

## 構成

| ディレクトリ | 内容 |
|---|---|
| `engine/` | git submodule → `pippirium/FloatSoda` の main を pin。フレームワーク変更は fork 側で行い、ここでは pin の更新だけを行う |
| `apps/WristAgent/` | 左腕の腕時計位置に AI エージェントの承認要求カードを出すアプリ(`apps/WristAgent/README.md` 参照) |
| `apps/ChromeView/` | Chrome(デスクトップ画面)を VR オーバーレイに表示するアプリ |

## 起動(SteamVR 起動中)

```powershell
dotnet run --project apps/WristAgent
dotnet run --project apps/ChromeView
```

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
