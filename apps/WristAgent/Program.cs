using System.Numerics;
using FloatSoda;
using FloatSoda.Abstractions.Geometries;
using FloatSoda.OVR;
using FloatSoda.OVR.Input;
using FloatSoda.OVR.Overlay;
using FloatSoda.Samples.WristAgent;
using FloatSoda.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (args.Contains("--voice-diagnostics"))
{
    var target = new WindowsCodexVoiceTarget();
    Console.WriteLine(target.Inspect());
    foreach (var name in target.DescribeButtons()) Console.WriteLine(name);
    return;
}

// 起動フラグ・位置指定ポートをGeneric Hostの構成パーサーへ渡さない。
var useDesktop = args.Contains("--desktop");
var useDebug = args.Contains("--debug");
var hostArgs = args.Where(a => a is not "--desktop" and not "--debug").ToArray();
var port = hostArgs.Length > 0 && int.TryParse(hostArgs[0], out var parsed) ? parsed : 8797;
if (hostArgs.Length > 0 && int.TryParse(hostArgs[0], out _)) hostArgs = hostArgs[1..];
var builder = Host.CreateApplicationBuilder(hostArgs);
using var debugLog = useDebug ? new WristDebugLog() : null;
if (debugLog is not null)
{
    builder.Logging.SetMinimumLevel(LogLevel.Debug);
    builder.Logging.AddProvider(debugLog);
}

builder.Services.AddFloatSoda(new FloatSodaOptions
{
    AppKey = new AppKey("FloatSoda.Samples.WristAgent"),
});

using var host = builder.Build();

// ブローカーと HTTP サーバーの生成。
// 既存の位置引数でポートを指定できる。
var app = host.Services.GetRequiredService<FloatSodaApp>();
debugLog?.SetDispatcher(a => app.Post(a));
var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
var logger = loggerFactory.CreateLogger("WristAgent");
var broker = new ApprovalBroker(a => app.Post(a), loggerFactory.CreateLogger<ApprovalBroker>());
var input = useDesktop ? null : new WristInputService(broker, a => app.Post(a));
var server = new WristHttpServer(broker, port, logger: loggerFactory.CreateLogger<WristHttpServer>());
using var voice = new CodexVoiceController(new WindowsCodexVoiceTarget(), app.Post);
using var chatScroll = new CodexChatScrollService();
using var controllerShortcuts = new ControllerVoiceShortcuts(voice, app.Post, chatScroll.Request);
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
using var controllerStarted = lifetime.ApplicationStarted.Register(controllerShortcuts.Start);
using var controllerStopping = lifetime.ApplicationStopping.Register(() =>
{
    controllerShortcuts.Dispose();
    chatScroll.Dispose();
});

// Codex音声操作はデスクトップ・腕UIの両方で有効にする。
var card = new WristAgentCard(broker, input, voice, debugLog);
app.CreateWindow(useDesktop
    ? new DesktopWindow { Title = "WristAgent — Desktop", Child = card }
    : new DeviceTrackedWindow
    {
        Title = "WristAgent",
        Target = TrackedDevice.LeftController,
        Offset = new Vector3(-0.12f, 0, 0.22f),
        Dpm = new Dpm(4000),
        Child = card,
    });

server.Start();
voice.StartMonitoring();

// ホットキー保険: Ctrl+Alt+Y(はい) / N(いいえ) / C(続行) / T(OSK 入力)。
// どの保留中要求からも選択できない場合(リスト空)は何もしない。
var hotkeys = new GlobalHotkeys(new Dictionary<int, Action>
{
    ['Y'] = () => ResolveOldest("allow"),
    ['N'] = () => ResolveOldest("deny"),
    ['C'] = () => ResolveOldest("continue"),
    ['T'] = () =>
    {
        var pending = broker.Pending;
        if (pending.Count > 0)
        {
            input?.TryOpen(pending[0].Id);
        }
    },
}, a => app.Post(a));
if (OperatingSystem.IsWindows()) hotkeys.Start();

// 着信通知は腕表示のときだけ行う。
using var vibrator = useDesktop ? null : new WristVibrator(broker);
logger.LogInformation("起動: 表示={Mode}, ポート={Port}, デバッグ={Debug}",
    useDesktop ? "desktop" : "wrist", port, useDebug);

void ResolveOldest(string decision)
{
    var pending = broker.Pending;
    if (pending.Count > 0)
    {
        broker.Resolve(pending[0].Id, new WristAnswer(decision, null));
    }
}

await host.RunAsync();
