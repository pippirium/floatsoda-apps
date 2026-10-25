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

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddFloatSoda(new FloatSodaOptions
{
    AppKey = new AppKey("FloatSoda.Samples.WristAgent"),
});

using var host = builder.Build();

// ブローカーと HTTP サーバーの生成。
// コマンドライン/環境変数でポートを変えられるようにしておく。
var port = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : 8797;
var app = host.Services.GetRequiredService<FloatSodaApp>();

var broker = new ApprovalBroker(a => app.Post(a));
var input = new WristInputService(broker, a => app.Post(a));
var server = new WristHttpServer(broker, port);

// user run
app.CreateWindow(new DeviceTrackedWindow
{
    Title = "WristAgent",
    Target = TrackedDevice.LeftController,
    Offset = new Vector3(-0.12f, 0, 0.22f),
    Dpm = new Dpm(4000),
    Child = new WristAgentCard(broker, input),
});

server.Start();

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
            input.TryOpen(pending[0].Id);
        }
    },
}, a => app.Post(a));
hotkeys.Start();

// 着信通知(コントローラー振動)。
var vibrator = new WristVibrator(broker);

void ResolveOldest(string decision)
{
    var pending = broker.Pending;
    if (pending.Count > 0)
    {
        broker.Resolve(pending[0].Id, new WristAnswer(decision, null));
    }
}

await host.RunAsync();
