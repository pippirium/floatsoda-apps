using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FloatSoda.Samples.WristAgent;

/// <summary>
/// 腕 UI へ要求を配送する最小構成のローカル HTTP サーバー。
/// 127.0.0.1 のみで待ち受け、<c>POST /requests</c> を受け付けます。
/// URL ACL（admin 権限）不要のため HttpListener でなく TcpListener を使用します。
/// </summary>
public sealed class WristHttpServer
{
    private const int IncomingBufferSize = 64 * 1024;
    private readonly ApprovalBroker _broker;
    private readonly int _port;
    private readonly TimeSpan _waitTimeout;
    private readonly ILogger<WristHttpServer>? _logger;

    /// <summary>サーバーを生成します。</summary>
    /// <param name="broker">要求の配送先ブローカー。</param>
    /// <param name="port">待ち受けポート。</param>
    /// <param name="logger">HTTP の動作・エラーを記録するロガー。</param>
    /// <param name="waitTimeout">ユーザー返答の最大待ち時間。既定は5分。</param>
    public WristHttpServer(ApprovalBroker broker, int port, TimeSpan? waitTimeout = null, ILogger<WristHttpServer>? logger = null)
    {
        _broker = broker;
        _logger = logger;
        _port = port;
        _waitTimeout = waitTimeout ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>待ち受けをバックグラウンドで開始します。</summary>
    public void Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start();
        _logger?.LogInformation("HTTP 待ち受け: http://127.0.0.1:{Port}/requests", _port);

        _ = Task.Run(() => AcceptLoop(listener));
    }

    private async Task AcceptLoop(TcpListener listener)
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync();
            }
            catch (Exception e)
            {
                _logger?.LogError(e, "HTTP 接続の受け付けに失敗");
                return;
            }

            _ = Task.Run(() => HandleClient(client));
        }
    }

    private async Task HandleClient(TcpClient client)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var request = await ReadRequestAsync(stream);
                if (request is null)
                {
                    await WriteResponseAsync(stream, 400, "bad request");
                    return;
                }

                _logger?.LogDebug("HTTP {Method}", request.Method);

                if (request.Method == "GET" && request.Path == "/health")
                {
                    await WriteResponseAsync(stream, 200, """{"status":"ok"}""");
                    return;
                }

                if (request.Method == "POST" && request.Path == "/requests")
                {
                    var response = await HandleRequestAsync(request);
                    await WriteResponseAsync(stream, 200, JsonSerializer.Serialize(response, Json));
                    return;
                }

                await WriteResponseAsync(stream, 404, "not found");
            }
        }
        catch (Exception e)
        {
            _logger?.LogError(e, "HTTP 要求の処理に失敗");
        }
    }

    private async Task<AnswerPayload> HandleRequestAsync(HttpRequestLine request)
    {
        var incoming = JsonSerializer.Deserialize<RequestPayload>(request.Body ?? "{}", Json);
        if (incoming is null)
        {
            return new AnswerPayload("bad-request", null);
        }

        var askTask = _broker.AskAsync(new WristRequest(
            incoming.Id ?? Guid.NewGuid().ToString("n")[..12],
            incoming.Kind ?? "approve",
            incoming.Agent ?? "unknown",
            incoming.Tool ?? "",
            incoming.Detail ?? ""));

        var timedOut = await Task.WhenAny(askTask, Task.Delay(_waitTimeout)) != askTask;
        if (timedOut)
        {
            _logger?.LogWarning("要求への返答がタイムアウトしました");
            return new AnswerPayload("timeout", null);
        }

        var answer = await askTask;
        return new AnswerPayload(answer.Decision, answer.Value);
    }

    private static async Task<HttpRequestLine?> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[1024];
        var total = 0;
        var data = new byte[IncomingBufferSize];

        // ヘッダ終端(\r\n\r\n)を探す
        var headerEnd = -1;
        while (total < data.Length)
        {
            var read = await stream.ReadAsync(data.AsMemory(total, data.Length - total));
            if (read == 0) return null;
            total += read;

            for (var i = 0; i <= total - 4; i++)
            {
                if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n')
                {
                    headerEnd = i + 4;
                    break;
                }
            }

            if (headerEnd >= 0) break;
        }

        if (headerEnd < 0) return null;

        var headText = Encoding.UTF8.GetString(data, 0, headerEnd);
        var lines = headText.Split("\r\n");
        if (lines.Length == 0) return null;

        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 3) return null;

        var contentLength = 0;
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator < 0) continue;

            var name = line[..separator].Trim();
            if (name.Equals("content-length", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(line[(separator + 1)..].Trim(), out var length))
            {
                contentLength = length;
            }
        }

        if (contentLength <= 0)
        {
            return new HttpRequestLine(requestLine[0].ToUpperInvariant(), requestLine[1], null);
        }

        var body = new byte[contentLength];
        var bodyFromBuffer = Math.Min(contentLength, total - headerEnd);
        Array.Copy(data, headerEnd, body, 0, bodyFromBuffer);
        var received = bodyFromBuffer;
        while (received < contentLength)
        {
            var read = await stream.ReadAsync(body.AsMemory(received, contentLength - received));
            if (read == 0) break;
            received += read;
        }

        return new HttpRequestLine(requestLine[0].ToUpperInvariant(), requestLine[1],
            Encoding.UTF8.GetString(body, 0, received));
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var head = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\n"
            + $"Content-Type: application/json; charset=utf-8\r\n"
            + $"Content-Length: {bodyBytes.Length}\r\n"
            + $"Connection: close\r\n"
            + $"\r\n";
        var headBytes = Encoding.UTF8.GetBytes(head);
        await stream.WriteAsync(headBytes);
        await stream.WriteAsync(bodyBytes);
        await stream.FlushAsync();
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private sealed record HttpRequestLine(string Method, string Path, string? Body);

    private sealed record RequestPayload(string? Id, string? Kind, string? Agent, string? Tool, string? Detail);

    private sealed record AnswerPayload(string Decision, string? Value);
}
