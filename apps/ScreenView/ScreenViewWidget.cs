using FloatSoda.Elements;
using FloatSoda.Geometrics;
using FloatSoda.RenderObjects;
using FloatSoda.RenderObjects.Painting;
using FloatSoda.Widgets;
using FloatSoda.Widgets.Layout;
using SkiaSharp;

namespace FloatSoda.Apps.ScreenView;

/// <summary>
/// 指定されたウィンドウのライブキャプチャを自身の領域へ表示するStatefulWidgetです。
/// </summary>
/// <remarks>
/// <para>
/// フレームの供給は<see cref="WindowCaptureSession"/>が担い、このウィジェットは
/// 受け取った<see cref="SKImage"/>を<see cref="FrameImage"/>へ差し替えて表示します。
/// セッションは<see cref="MaxFps"/>の上限でフレームを供給します。
/// </para>
/// <para>
/// 操作部(クリック・スクロール)はここでは実装していません(表示専用)。
/// movable オーバーレイのポインタ入力(コントローラーレイ + 左トリガー)が
/// FloatSoda に統合済みです。
/// </para>
/// </remarks>
public record ScreenViewWidget : StatefulWidget<ScreenViewWidget>
{
    /// <summary>キャプチャするウィンドウのハンドル。</summary>
    public required nint WindowHandle { get; init; }

    /// <summary>状態更新の上限(fps)。</summary>
    /// <remarks>既定は30。0以下はここで切って 30 とみなす。</remarks>
    public int MaxFps { get; init; } = 30;

    /// <summary>画像を表示する方法。</summary>
    public BoxFit Fit { get; init; } = BoxFit.Contain;

    /// <summary>画像を自身の領域内へ配置する位置。</summary>
    public Alignment Alignment { get; init; } = Alignment.Center;

    /// <summary>キャプチャ開始前に表示するテキスト。</summary>
    public string WaitMessage { get; init; } = "キャプチャを準備しています…";

    /// <summary>外部で作成したキャプチャ セッション。既定は null(内部で生成します)。</summary>
    /// <remarks>
    /// 対象切替(OverlayManipulator → <see cref="WindowCaptureSession.SwitchTarget"/>)を
    /// セッション越しに行いたい場合に注入します。注入側は <see cref="WindowCaptureSession.Start"/> を
    /// 呼ばず、<see cref="State"/> 側がハンドル指定で開始します。
    /// </remarks>
    public WindowCaptureSession? Session { get; init; }

    /// <inheritdoc />
    public override State<ScreenViewWidget> CreateState() => new ScreenViewState();

    private sealed class ScreenViewState : State<ScreenViewWidget>
    {
        private WindowCaptureSession? _session;
        private bool _ownedSession;
        private SKImage? _frame;
        private string? _message;

        public override void InitState()
        {
            if (Widget!.WindowHandle == 0)
            {
                _message = "キャプチャ対象のウィンドウが指定されていません。";
                return;
            }

            _message = Widget!.WaitMessage;
            _ownedSession = Widget!.Session is null;
            _session = Widget!.Session ?? new WindowCaptureSession(Widget!.MaxFps);
            _session.FrameUpdated += OnFrame;
            _session.Failed += message =>
            {
                // オーバーレイ表示に加えてコンソールにも出す(切分け用)。
                Console.Error.WriteLine($"[ScreenView] {message}");
                SetState(() => _message = message);
            };
            _session.Start(Widget!.WindowHandle);
        }

        /// <summary>キャプチャ スレッドから呼ばれます(WatchWidget と同じ Timer + SetState のパターン)。</summary>
        private void OnFrame(SKImage frame)
        {
            SetState(() =>
            {
                _frame = frame;
                _message = null;
            });
        }

        public override Widget Build(IBuildContext context)
        {
            if (_frame is not null)
            {
                return new FrameImage
                {
                    Image = _frame!,
                    Fit = Widget!.Fit,
                    Alignment = Widget!.Alignment,
                };
            }

            return new Align
            {
                Alignment = Alignment.Center,
                Child = new Text(_message ?? Widget!.WaitMessage),
            };
        }

        public override void Dispose()
        {
            if (_session is not null)
            {
                _session.FrameUpdated -= OnFrame;
                // 外部注入セッションは提供側の寿命で開放する。
                if (_ownedSession) _session.Dispose();
                _session = null;
            }
            base.Dispose();
        }
    }
}

/// <summary>
/// 受け取った<see cref="SKImage"/>を自身の領域へ描画する、子を持たないウィジェット。
/// </summary>
/// <remarks>
/// 静的画像用の Image ウィジェットと同じ <see cref="RenderImage"/> で描画しますが、
/// ImageProvider とキャッシュを介さないライブ配信専用の薄いラップです。
/// フレームの寿命はキャプチャ セッション(リング)が持ちます。
/// </remarks>
public sealed record FrameImage : LeafRenderObjectWidget<RenderImage>
{
    /// <summary>描画する画像。キャプチャ側が寿命を保持します(約3フレーム)。</summary>
    public required SKImage Image { get; init; }

    /// <summary>画像を自身の領域へ収める方法。</summary>
    public BoxFit Fit { get; init; } = BoxFit.Contain;

    /// <summary>収めた画像を自身の領域内へ配置する位置。</summary>
    public Alignment Alignment { get; init; } = Alignment.Center;

    /// <inheritdoc />
    public override RenderImage CreateRenderObject() => new()
    {
        Image = Image,
        Fit = Fit,
        Alignment = Alignment,
    };

    /// <inheritdoc />
    public override void UpdateRenderObject(RenderImage renderObject)
    {
        renderObject.Image = Image;
        renderObject.Fit = Fit;
        renderObject.Alignment = Alignment;
    }
}
