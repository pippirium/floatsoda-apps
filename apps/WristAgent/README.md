# FloatSoda.Samples.WristAgent

左腕(左コントローラー)の腕時計位置に、AI エージェント(Codex など)の承認要求を
カードとして出し、レーザーポインターで はい / いいえ / 続行 を選べるアプリです。
_placement_: `floatsoda-apps` リポジトリの `apps/WristAgent`(移植元: FloatSoda の
`samples/FloatSoda.Samples.WristAgent`)

## 起動

```powershell
dotnet run --project apps/WristAgent
# ポートを変える場合
dotnet run --project apps/WristAgent -- 8801
```

SteamVR が起動している必要があります。起動すると左腕に `WristAgent` カードが出ます。

## HTTP プロトコル

`http://127.0.0.1:{port}/requests` へ POST します(ローカルのみ)。

```jsonc
// 要求
{ "id": "abc123", "kind": "approve", "agent": "codex", "tool": "Bash", "detail": "git push …" }
```

- `kind`: `approve`(承認) / `continue`(継続) / `question`(自由入力) / `notify`(通知のみ)
- 応答はユーザーが腕 UI で選択するまで保持され、選択後に返ります。
  最大待ち時間は既定 5 分で、超えると `{ "decision": "timeout" }` を返します。

```jsonc
// 応答
{ "decision": "allow" }          // はい / continue
{ "decision": "deny" }           // いいえ
{ "decision": "text", "value": "…" }   // フリーテキスト(後述の OSK 連携)
{ "decision": "timeout" }        // 未応答
```

試すには:

```powershell
curl http://127.0.0.1:8797/requests -d "{\"id\":\"t1\",\"kind\":\"approve\",\"agent\":\"test\",\"tool\":\"Bash\",\"detail\":\"echo hello\"}"
```

## ドキュメント(text)入力(SteamVR OSK)

腕カードの[入力]ボタンで SteamVR のオーバーレイ用仮想キーボード(OSK)が開きます。
確定(Done)すると入力テキストが `decision: "text"` として要求に返ります。
hooks 経路ではこのテキストを deny + メッセージとしてエージェントへ伝えます
(hooks の語彙に自由テキストがないため)。MCP経由では自由テキストのまま使えます。

## エージェントへの接続(tools/WristAgentBridge)

`tools/WristAgentBridge` は stdin の hooks イベント JSON を腕 UI へ転送し、
ユーザーの選択を hooks 応答 JSON に変換する CLI です。応答が得られない場合は
何も出力せずに終了するため、エージェントは通常の承認フローにフォールバックします。

Codex の設定例(`~/.codex/hooks.json`)。

```json
{
  "hooks": {
    "PermissionRequest": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "dotnet run --project C:/Users/haru/repo/floatsoda-apps/tools/WristAgentBridge --format codex",
            "timeout": 600
          }
        ]
      }
    ]
  }
}
```

Claude Code の設定例(`~/.claude/settings.json` の hooks)。

```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Bash|Edit|Write",
        "hooks": [
          {
            "type": "command",
            "command": "dotnet run --project C:/Users/haru/repo/floatsoda-apps/tools/WristAgentBridge --format claude",
            "timeout": 600
          }
        ]
      }
    ]
  }
}
```

### MCP による自由テキスト指示(serve-mcp)

`tools/WristAgentBridge` には stdio MCP サーバーモードもあります。
Codex / Claude Code の `mcp_servers` に登録すると、エージェントは
`ask_wrist(question)` ツールで腕 UI へ質問を投げ、[はい] / [いいえ] /
OSK 入力テキストを文字列として受け取れます。

```jsonc
// Codex: ~/.codex/config.toml
// [mcp_servers.wristagent]
// command = "dotnet"
// args = ["run", "--project", "C:/Users/haru/repo/floatsoda-apps/tools/WristAgentBridge", "serve-mcp"]

// Claude Code: .mcp.json
{
  "mcpServers": {
    "wristagent": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/Users/haru/repo/floatsoda-apps/tools/WristAgentBridge", "serve-mcp"]
    }
  }
}
```

注意 (hooks 設定の続き):

- hooks を変更したら Codex では `/hooks` で trust review を通す必要があります。
- `timeout` は腕 UI の待ち時間(既定 5 分)より長めに設定してください。
- 本サンプルは `MakeOverlaysInteractiveIfVisible` を立てない
  `ControllerRayPointerSource` 経由の入力のため、SteamVR の入力横取りは発生しません。

- hooks 経由で `ask_wrist` ツール自体への承認要求が出た場合は、
  `PermissionRequest` の `matcher` に `mcp__wristagent__ask_wrist` を許可して
  承認ループを避けてください(MCP サーバー名は設定名に依存します)。

### ホットキー保険

サンプル起動中はグローバルホットキー(Ctrl+Alt+Y/N/C/T)も有効です。
どちらの保留中要求にも選択できる最古の要求に作用し、キーは他アプリへ漏れません。

| キー | 動作 |
|---|---|
| Ctrl+Alt+Y | はい(allow) |
| Ctrl+Alt+N | いいえ(deny) |
| Ctrl+Alt+C | 続行(continue) |
| Ctrl+Alt+T | 最古の要求へ OSK を開く |

## 既知の制限

- 入力は左トリガーのみ(右ボタン・スクロールなし)。
- 保留中の要求は複数もてますが、返答は常に選択した要求に対して
  行われ、ホットキーは最古の要求に作用します。
