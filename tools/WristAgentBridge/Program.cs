using System.Text;
using System.Text.Json;

// ---------------------------------------------------------------------------
// WristAgentBridge — hooks 設定から WristAgent(腕オーバーレイ)へ要求を投げる CLI。
//
// stdin に hooks イベント JSON を受け取り、WristAgent の HTTP サーバー
// (POST /requests) へ転送して、ユーザーの選択を hooks の応答 JSON として
// stdout に出力します。
//
// - Codex:       ~/.codex/hooks.json の PermissionRequest / Stop から呼ぶ前提
// - Claude Code: hooks の PreToolUse / Stop から呼ぶ前提
// 応答が得られない(タイムアウト/サーバー停止)場合は何も出力せず exit 0。
// その場合 Codex / Claude Code は通常の承認フローへフォールバックします。
// ---------------------------------------------------------------------------

var endpoint = Environment.GetEnvironmentVariable("WRIST_AGENT_ENDPOINT")
               ?? "http://127.0.0.1:8797";
var format = "codex";
var agent = "codex";

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--format" when i + 1 < args.Length:
            format = args[++i];
            agent = format == "claude" ? "claude-code" : "codex";
            break;
        case "--agent" when i + 1 < args.Length:
            agent = args[++i];
            break;
        case "--endpoint" when i + 1 < args.Length:
            endpoint = args[++i];
            break;
    }
}

// MCP サーバーモード: Codex / Claude Code の mcp_servers から stdio で起動される。
// tools/call ask_wrist(question) を腕 UI(POST /requests)へ中継し、
// ユーザーの返答を文字列で返す。
if (Array.IndexOf(args, "serve-mcp") >= 0)
{
    return await RunMcpServerAsync();
}

string inputJson;
using (var stdin = Console.OpenStandardInput())
using (var reader = new StreamReader(stdin, Encoding.UTF8))
{
    var buffered = await reader.ReadToEndAsync();
    inputJson = buffered.Length == 0 ? "{}" : buffered;
}

var (tool, detail) = ExtractTool(inputJson);

var postBody = JsonSerializer.Serialize(new
{
    id = Guid.NewGuid().ToString("n")[..12],
    kind = "approve",
    agent,
    tool,
    detail,
});

try
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    using var content = new StringContent(postBody, Encoding.UTF8, "application/json");
    using var response = await http.PostAsync($"{endpoint.TrimEnd('/')}/requests", content);
    if (!response.IsSuccessStatusCode)
    {
        return 0; // fail open
    }

    var payload = await response.Content.ReadAsStringAsync();
    var json = JsonSerializer.Deserialize<BridgeResponse>(payload);
    if (json is null)
    {
        return 0;
    }

    // ユーザーが入力したテキストは hooks の語彙には載せられないため
    // deny + 応答メッセージとしてエージェントへ伝える。
    if (json.Decision == "text")
    {
        WriteDecision("deny", $"WristAgent でのユーザー入力: {json.Value}");
        return 0;
    }

    switch (json.Decision)
    {
        case "allow":
            WriteDecision("allow", "WristAgent で[はい]が選択されました");
            return 0;
        case "deny":
            WriteDecision("deny", "WristAgent で[いいえ]が選択されました");
            return 0;
        default:
            return 0; // continue / timeout は決定しない(通常のフローにフォールバック)
    }
}
catch (Exception)
{
    // サーバー停止やタイムアウト時は決定せず、通常の承認フローへ落ちる。
    return 0;
}

return 0;

async Task<int> RunMcpServerAsync()
{
    while (await Console.In.ReadLineAsync() is { } line)
    {
        if (line.Trim().Length == 0) continue;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            continue;
        }

        using (document)
        {
            var root = document.RootElement;

            if (!root.TryGetProperty("id", out var id))
            {
                continue; // notification (notifications/initialized など) には返答しない
            }

            var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;

            object? result = null;
            object? error = null;

            switch (method)
            {
                case "initialize":
                    result = new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities = new { tools = new { } },
                        serverInfo = new { name = "wristagent-bridge", version = "0.1.0" },
                    };
                    break;

                case "tools/list":
                    result = new
                    {
                        tools = new[]
                        {
                            new
                            {
                                name = "ask_wrist",
                                description = "腕オーバーレイ(WristAgent)に質問を投げ、ユーザーの返答を文字列として受け取ります。" +
                                              "返答は「はい」「いいえ」もしくは OSK で入力された自由テキストです。",
                                inputSchema = new
                                {
                                    type = "object",
                                    properties = new { question = new { type = "string", description = "ユーザーに投げる質問。" } },
                                    required = new[] { "question" },
                                },
                            },
                        },
                    };
                    break;

                case "tools/call":
                    var question = "";
                    if (root.TryGetProperty("params", out var p) &&
                        p.TryGetProperty("arguments", out var a) &&
                        a.TryGetProperty("question", out var q))
                    {
                        question = q.GetString() ?? "";
                    }

                    var answer = await AskWristAsync(question);
                    result = new
                    {
                        content = new[] { new { type = "text", text = answer } },
                    };
                    break;

                default:
                    error = new { code = -32601, message = $"method not found: {method}" };
                    break;
            }

            object response = error is null
                ? (object)new { jsonrpc = "2.0", id, result }
                : (object)new { jsonrpc = "2.0", id, error };

            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response));
        }
    }

    return 0;
}

async Task<string> AskWristAsync(string question)
{
    var postBody = JsonSerializer.Serialize(new
    {
        id = Guid.NewGuid().ToString("n")[..12],
        kind = "question",
        agent,
        tool = "",
        detail = question,
    });

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    using var content = new StringContent(postBody, Encoding.UTF8, "application/json");
    using var response = await http.PostAsync($"{endpoint.TrimEnd('/')}/requests", content);
    if (!response.IsSuccessStatusCode)
    {
        return "エラー: WristAgent に接続できませんでした(通常のやり取りに戻ってください)";
    }

    var payload = await response.Content.ReadAsStringAsync();
    var json = JsonSerializer.Deserialize<BridgeResponse>(payload);

    return json?.Decision switch
    {
        "allow" => "はい",
        "deny" => "いいえ",
        "text" => json.Value ?? "",
        "timeout" => "無応答(タイムアウト)",
        _ => "無応答",
    };
}

void WriteDecision(string behavior, string message)
{
    var result = format == "claude"
        ? JsonSerializer.Serialize(new
        {
            hookSpecificOutput = new
            {
                hookEventName = "PreToolUse",
                permissionDecision = behavior,
                permissionDecisionReason = message,
            },
        })
        : JsonSerializer.Serialize(new
        {
            hookSpecificOutput = new
            {
                hookEventName = "PermissionRequest",
                decision = new
                {
                    behavior,
                    message,
                },
            },
        });

    Console.Out.WriteLine(result);
}

(string Tool, string Detail) ExtractTool(string json)
{
    try
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var toolName = root.TryGetProperty("tool_name", out var tn) ? tn.GetString() ?? "" : "";
        var description = root.TryGetProperty("tool_input", out var ti)
            ? (ti.TryGetProperty("description", out var d) ? d.GetString() : null)
              ?? (ti.TryGetProperty("command", out var c) ? c.GetString() : null)
              ?? ti.ToString()
            : "";
        var cwd = root.TryGetProperty("cwd", out var cw) ? cw.GetString() ?? "" : "";

        var detailSource = string.IsNullOrEmpty(description) ? root.ToString() : description;
        var detail = detailSource.Length > 400 ? detailSource[..400] + "…" : detailSource;
        var prefix = string.IsNullOrEmpty(cwd) ? "" : $"{Path.GetFileName(cwd.TrimEnd(Path.DirectorySeparatorChar))} · ";

        return (toolName, prefix + detail);
    }
    catch (JsonException)
    {
        return ("", json.Length > 400 ? json[..400] : json);
    }
}

public sealed record BridgeResponse(string Decision, string? Value);
