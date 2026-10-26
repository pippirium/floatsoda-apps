using FloatSoda;
using FloatSoda.Abstractions.Geometries;
using FloatSoda.OVR;
using FloatSoda.Apps.ScreenView;
using FloatSoda.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SkiaSharp;

// 自分のHMD上で任意のウィンドウの画面を見るアプリ。
// Windows Graphics Capture でアプリ単位の画面を取り込み、FloatSoda の
// RenderImage でリアルタイムに表示します。キャプチャ対象はオーバーレイ上の
// ピッカー(上部の [一覧])から選べます。
//
// 使い方:
//   --desktop            SteamVRの代わりにデスクトップウィンドウで表示(HMDが不要な目視確認用)
//   --dashboard          SteamVR ダッシュボード上のオーバーレイに表示(--world はこの別名)
//                        引数なし(SteamVR 自動起動時を含む)の既定はワールド空間への直接表示
//   --pos "x,y,z"        --world 時の初期配置座標(プレイエリア中央原点。既定は 0,1,-1)
//   --yaw <度>           --world 時の初期水平回転(度)。既定は 0(正面を向く)
//   --list-windows       候補ウィンドウの一覧を出して終了する
//   --window <タイトル>   キャプチャするウィンドウをタイトル部分一致で指定(既定はchromeの先頭)
//   --pid <数値>          キャプチャするウィンドウをプロセスIDで指定
//   --process <名前>      対象プロセス名(既定は chrome)
//   --fps <数値>          表示更新の上限(既定は 30)
//   --toggle <割当>       パネル表示/非表示の割当(形式: <手>:<ボタン>[+grip][@<ms>])
//                         手=L|R, ボタン=stick|a|trigger|menu|grip。既定は L:stick+grip@700
//                         (左グリップを握ったまま左スティック押し込み 0.7秒長押し)
//   --switch <割当>       ピッカー開閉の割当(形式は --toggle と同じ)。既定は R:stick+grip@300
//   --save-frames <数値>  FloatSodaを起動せず、キャプチャ N 枚をPNG保存して終了する(デバッグ用)
//
// HMD 上での操作(--world のとき、OverlayManipulator):
//   グリップ(左右どちらでも)   パネルを掴んで移動
//   右スティック上下(掴まず)    パネルの幅(＝高さは縦横比)を拡縮(上=拡大)
//   左 グリップ+スティック押し込み 0.7秒長押し  パネルの表示/非表示(--toggle で変更可)
//   右 グリップ+スティック押し込み 0.3秒長押し  キャプチャ対象を選ぶピッカーの開閉(--switch で変更可)
//   オーバーレイ上の [一覧] / 行 / [◀][▶] / [再読込] / [閉じる] は
//   ダッシュボード オーバーレイでは SteamVR レーザー、movable 窓では左トリガーで押せる

var useDesktop = args.Contains("--desktop");
var useDashboard = args.Contains("--dashboard") || args.Contains("--world");
// 引数なし(SteamVR 自動起動時を含む)ではワールド表示を既定にする(--dashboard/--desktop で例外)。

// --pos "x,y,z" / --yaw <度> でワールド配置を調整できる(既定はプレイエリア中央から前方1m・高さ1m)。
var posIndex = Array.IndexOf(args, "--pos");
var yawIndex = Array.IndexOf(args, "--yaw");
var worldPosition = System.Numerics.Vector3.Zero;
var worldYawDeg = 0f;
if (posIndex >= 0 && posIndex + 1 < args.Length)
{
    var parts = args[posIndex + 1].Split(',');
    if (parts.Length == 3
        && float.TryParse(parts[0], out var px) && float.TryParse(parts[1], out var py) && float.TryParse(parts[2], out var pz))
    {
        worldPosition = new System.Numerics.Vector3(px, py, pz);
    }
}
if (yawIndex >= 0 && yawIndex + 1 < args.Length && float.TryParse(args[yawIndex + 1], out var yaw))
{
    worldYawDeg = yaw;
}

var listIndex = Array.IndexOf(args, "--list-windows");
var saveIndex = Array.IndexOf(args, "--save-frames");

string GetOptionValueAfter(string name) =>
    Array.IndexOf(args, name) is var i && i >= 0 && i + 1 < args.Length ? args[i + 1] : string.Empty;

var titleFilter = args.Contains("--window") ? GetOptionValueAfter("--window") : null;
var processFilter = args.Contains("--process") ? GetOptionValueAfter("--process") : "chrome";
var pidFilter = uint.TryParse(GetOptionValueAfter("--pid"), out var parsedPid) ? parsedPid : 0u;
var fpsLimit = int.TryParse(GetOptionValueAfter("--fps"), out var parsedFps) && parsedFps > 0 ? parsedFps : 30;
// パネルの表示/非表示はコントローラ割当。既定は L:stick+grip@700(VRChat の操作と被りにくい組合せ)。
var toggleSpec = args.Contains("--toggle") ? GetOptionValueAfter("--toggle") : "L:stick+grip@700";
var switchSpec = args.Contains("--switch") ? GetOptionValueAfter("--switch") : "R:stick+grip@300";

var allWindows = WindowLocator.EnumerateVisibleWindows();

if (listIndex >= 0)
{
    foreach (var w in allWindows)
    {
        var marker = w.ProcessName.Equals(processFilter, StringComparison.OrdinalIgnoreCase) ? " * " : "   ";
        Console.WriteLine($"{marker}PID={w.ProcessId,8} HWND=0x{w.Handle:X} [{w.ProcessName}] {w.Title}");
    }
    return 0;
}

var target = WindowLocator.Pick(allWindows, titleFilter, processFilter, pidFilter) ?? throw new InvalidOperationException(
    $"キャプチャ対象のウィンドウが見つかりませんでした。ウィンドウやプロセスは、--list-windows で一覧できます。");
Console.WriteLine($"キャプチャ対象: PID={target.ProcessId} HWND=0x{target.Handle:X} [{target.ProcessName}] {target.Title}");

if (saveIndex >= 0)
{
    return await SaveFramesAsync(target, GetOptionValueAfter("--save-frames"));
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddFloatSoda(new FloatSodaOptions
{
    // SteamVR vrmanifest(screenview.vrmanifest)の app_key と一致させ、IdentifyApplication /
    // 自動起動連携が正しく機能するようにする。
    AppKey = new AppKey(SteamVrAutoLaunch.AppKey),
});

using var host = builder.Build();
var app = host.Services.GetRequiredService<FloatSodaApp>();

// プログラムで保持して対象切替から使うため外部生成。Start はウィジェット側が行う。
var captureSession = new WindowCaptureSession(fpsLimit);

// 候補の列挙は Win32 のウィンドウ + プロセス参照を伴うため裏スレッドで走らせ、
// 結果は Post でメインスレッドへ返す(メインスレッド STA で Win32 列挙を直接やらない)。
// 差し出し口(OfferCandidateResult)はウィジェットがマウントされたときに State 側がバインドするため、
// widget を先に宣言してからこの関数を定義する(ローカル関数を式の中で先に参照できない)。
var widget = new ScreenViewWidget
{
    WindowHandle = target.Handle,
    MaxFps = fpsLimit,
    // 対象切替(コントローラ割当・ピッカー)で状態セッションを操作できるよう、外部で作って注入する。
    Session = captureSession,
    CandidateEnumerator = WindowTargets.EnumerateCandidates,
};

void RefreshCandidates(long generation) => _ = Task.Run(() =>
{
    CandidateRefreshResult result;
    try
    {
        result = CandidateRefreshResult.Succeeded(WindowTargets.EnumerateCandidates());
    }
    catch (Exception e)
    {
        var message = string.IsNullOrWhiteSpace(e.Message) ? e.GetType().Name : e.Message;
        result = CandidateRefreshResult.Failed(message);
    }

    app.Post(() => widget.OfferCandidateResult?.Invoke(generation, result));
});

widget.RequestCandidateRefresh = RefreshCandidates;

// SteamVRが起動していることが前提。--desktop のときはモニタ上のデスクトップウィンドウに、
// --dashboard のときはSteamVR ダッシュボードのオーバーレイに、既定(--world 相当)は
// SteamVR Homeなどコントローラを開かずにワールド空間へ直接表示する。
app.CreateWindow(
    useDesktop
        ? new DesktopWindow { Title = "Screen View", Child = widget }
        : useDashboard
            ? new DashboardWindow { Dpm = new Dpm(1600), Title = "Screen View", Child = widget }
            : new WorldSpaceWindow
            {
                Title = "Screen View",
                Size = new SKSize(1920, 1080),
                Child = widget,
                Position = worldPosition == System.Numerics.Vector3.Zero
                    ? new System.Numerics.Vector3(0f, 1f, -1f)
                    : worldPosition,
                Rotation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, worldYawDeg * MathF.PI / 180f),
            });

// --world ではワールドに浮かんだパネルをコントローラで捉えられるようにする(掴み移動・拡縮・ピッカー)。
OverlayManipulator? manipulator = null;
if (!useDesktop)
{
    // SteamVR 自動起動の vrmanifest 登録は OpenVR コンテキストが整うのを待って行うので背景で試す(非致命)。
    _ = Task.Run(SteamVrAutoLaunch.Register);

    manipulator = new OverlayManipulator("Screen View", toggleSpec, switchSpec);
    // 割当の意味は「次の候補へ巡回」から「ピッカーの開閉」へ変更した。自動巡回は廃止する
    // (一覧が出るため巡回は不要)。入力ループは別スレッドなので Post でメインスレッドへ転送する。
    manipulator.TargetSwitchRequested += () => app.Post(() => widget.TogglePickerRequested?.Invoke());
    manipulator.Start();
}

try
{
    await host.RunAsync();
}
finally
{
    manipulator?.Dispose();
    captureSession.Dispose();
}
return 0;

/// <summary>キャプチャ1枚をPNGとして保存するデバッグ用の動作チェック パス。</summary>
/// <remarks>SteamVR に接続しません。WGC の取り込みと D3D11(staging / Map / Copy) 経路の検証用です。</remarks>
static async Task<int> SaveFramesAsync(WindowInfo target, string framesOption)
{
    var sampleDirectory = Path.Combine(Path.GetTempPath(), "FloatSodaScreenView");
    Directory.CreateDirectory(sampleDirectory);

    var count = int.TryParse(framesOption, out var parsedCount) && parsedCount > 0 ? parsedCount : 1;
    var remaining = count;

    var session = new WindowCaptureSession(fpsLimit: 5);
    var finishedTask = new TaskCompletionSource();

    session.FrameUpdated += image =>
    {
        var file = Path.Combine(sampleDirectory, $"frame-{Random.Shared.Next():X8}.png");
        using var data = image.Encode();
        using var fileStream = File.OpenWrite(file);
        data.SaveTo(fileStream);
        Console.WriteLine($"保存しました: {file}");
        if (Interlocked.Decrement(ref remaining) == 0)
        {
            finishedTask.TrySetResult();
        }
    };
    session.Failed += message => Console.Error.WriteLine($"失敗: {message}");
    session.Start(target.Handle);

    // 最大 15秒 待つ
    var finished = await Task.WhenAny(finishedTask.Task, Task.Delay(15_000));
    session.Dispose();

    if (finished != finishedTask.Task)
    {
        Console.Error.WriteLine("タイムアウト: 15秒以内にフレームを受け取れませんでした。");
        return 1;
    }

    return 0;
}
