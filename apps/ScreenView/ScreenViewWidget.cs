using FloatSoda.Elements;
using FloatSoda.Gesture;
using FloatSoda.Geometrics;
using FloatSoda.Painting;
using FloatSoda.RenderObjects;
using FloatSoda.RenderObjects.Painting;
using FloatSoda.Widgets;
using FloatSoda.Widgets.Gesture;
using FloatSoda.Widgets.Layout;
using FloatSoda.Widgets.Paint;
using SkiaSharp;

namespace FloatSoda.Apps.ScreenView;

/// <summary>ウィンドウ候補の列挙結果。成功時は候補一覧、失敗時はエラー文を保持します。</summary>
public sealed record CandidateRefreshResult
{
    private CandidateRefreshResult(IReadOnlyList<WindowInfo>? candidates, string? error)
    {
        Candidates = candidates;
        Error = error;
    }

    /// <summary>列挙に成功したときの候補一覧。失敗時は <see langword="null"/> です。</summary>
    public IReadOnlyList<WindowInfo>? Candidates { get; }

    /// <summary>列挙に失敗したときの説明。成功時は <see langword="null"/> です。</summary>
    public string? Error { get; }

    /// <summary>成功した候補一覧を作成します。</summary>
    public static CandidateRefreshResult Succeeded(IReadOnlyList<WindowInfo> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return new CandidateRefreshResult(candidates, null);
    }

    /// <summary>失敗した候補列挙結果を作成します。</summary>
    public static CandidateRefreshResult Failed(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new CandidateRefreshResult(null, error);
    }
}

/// <summary>
/// 指定されたウィンドウのライブキャプチャを自身の領域へ表示し、上部のヘッダーバーと
/// オーバーレイ上のシートからキャプチャ対象を選べるようにするStatefulWidgetです。
/// </summary>
/// <remarks>
/// <para>
/// フレームの供給は<see cref="WindowCaptureSession"/>が担い、このウィジェットは
/// 受け取った<see cref="SKImage"/>を<see cref="FrameImage"/>へ差し替えて表示します。
/// セッションは<see cref="MaxFps"/>の上限でフレームを供給します。
/// </para>
/// <para>
/// 対象切替はセッションを作り直さず<see cref="WindowCaptureSession.SwitchTarget"/>を呼ぶだけ。
/// プール再作成による最初の数フレームの空白は、シートで覆われているため視覚的ノイズになりません。
/// </para>
/// <para>
/// 操作部は<see cref="GestureDetector"/>で実装しています。movable オーバーレイのポインタ入力
/// (コントローラーレイ + 左トリガー)が FloatSoda に統合済みです。
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
    /// 対象切替(<see cref="WindowCaptureSession.SwitchTarget"/>)を
    /// セッション越しに行いたい場合に注入します。注入側は <see cref="WindowCaptureSession.Start"/> を
    /// 呼ばず、<see cref="State"/> 側がハンドル指定で開始します。
    /// </remarks>
    public WindowCaptureSession? Session { get; init; }

    /// <summary>候補ウィンドウを列挙する関数。ピッカーを開くときと [再列挙] のときだけ呼ばれます。</summary>
    /// <remarks>
    /// Win32 のウィンドウ/プロセス列挙を含むため、呼び出し側はメインループを止めないよう
    /// バックグラウンド スレッドで実行し、結果を <see cref="OfferCandidateResult"/> で返します。
    /// </remarks>
    public Func<IReadOnlyList<WindowInfo>>? CandidateEnumerator { get; init; }

    /// <summary>候補の再列挙を裏スレッドへ要求する関数。既定は <see cref="CandidateEnumerator"/> をその場で実行する。</summary>
    /// <remarks>
    /// 指定した場合、ウィジェットは自分で列挙せずこの関数だけを呼ぶ。呼び出し側は完了後に
    /// <see cref="OfferCandidateResult"/> をメインスレッドから呼んで結果を返すこと。
    /// 裏スレッドから呼ばれるため、State のものではなく呼び出し側が用意した
    /// <c>FloatSodaApp.Post</c> を使う必要がある。
    /// </remarks>
    public Action<long>? RequestCandidateRefresh { get; set; }

    /// <summary>ピッカーの開閉を外部(コントローラ割当)から要求するための口。</summary>
    /// <remarks>
    /// ウィジェットが <c>null</c> でない側へマウント中に限り設定される。コントローラ入力ループ
    /// (メインループ以外のスレッド)から呼ぶ場合は、必ず <c>FloatSodaApp.Post</c> 経由で
    /// メインスレッドへ転送してください。
    /// </remarks>
    public Action? TogglePickerRequested { get; set; }

    /// <summary>要求番号と候補の列挙結果を渡す口。</summary>
    /// <remarks>
    /// 裏スレッドで列挙した結果を <c>FloatSodaApp.Post</c> 経由でメインスレッドから呼ぶ。
    /// シートが閉じているときは無視する(開き直し時に必ず再列挙するため、古い結果を保持しない)。
    /// </remarks>
    public Action<long, CandidateRefreshResult>? OfferCandidateResult { get; set; }

    /// <summary>シートを開いているかどうか。開閉判定は状態側が持つため、参照のみ提供する。</summary>
    public Func<bool>? IsPickerOpen { get; set; }

    /// <summary>ピッカー内部の再読込要求をStateへ結び付けるデリゲート。</summary>
    internal Action? RefreshPickerRequested { get; set; }

    /// <summary>ピッカー内部の閉じる要求をStateへ結び付けるデリゲート。</summary>
    internal Action? ClosePickerRequested { get; set; }

    /// <summary>ピッカー内部の対象選択をStateへ結び付けるデリゲート。</summary>
    internal Action<WindowInfo>? PickWindowRequested { get; set; }

    /// <inheritdoc />
    public override State<ScreenViewWidget> CreateState() => new ScreenViewState();

    private sealed class ScreenViewState : State<ScreenViewWidget>
    {
        private readonly Action _refreshPickerCallback;
        private readonly Action _closePickerCallback;
        private readonly Action<WindowInfo> _pickWindowCallback;
        private readonly Action _togglePickerCallback;
        private readonly Action<long, CandidateRefreshResult> _offerCandidateResultCallback;
        private readonly Func<bool> _isPickerOpenCallback;
        private WindowCaptureSession? _session;
        private bool _ownedSession;
        private SKImage? _frame;
        private string? _message;
        private nint _currentHandle;
        private bool _pickerOpen;
        private IReadOnlyList<WindowInfo> _candidates = [];
        private string? _candidateMessage;
        private bool _isCandidateRefreshing;
        private long _candidateRequestGeneration;

        public ScreenViewState()
        {
            _refreshPickerCallback = RefreshCandidates;
            _closePickerCallback = ClosePicker;
            _pickWindowCallback = PickWindow;
            _togglePickerCallback = TogglePicker;
            _offerCandidateResultCallback = OfferCandidateResult;
            _isPickerOpenCallback = () => _pickerOpen;
        }

        public override void InitState()
        {
            _currentHandle = Widget!.WindowHandle;
            if (_currentHandle == 0)
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
            _session.Start(_currentHandle);

            // コントローラの長押し割当はバックグラウンド スレッドで発火するため、
            // SetState を呼ぶのは常にメインスレッドに限定する。要求口はウィジェットへ差し出す。
            BindHooks();
        }

        public override void DidUpdateWidget(ScreenViewWidget oldWidget)
        {
            // ウィジェットが差し替わったら、要求口を新しい側へ結び直す。
            UnbindHooks(oldWidget);
            BindHooks();
        }

        /// <summary>現在のウィジェットへ外部からの要求口を登録する。</summary>
        private void BindHooks()
        {
            Widget!.TogglePickerRequested = _togglePickerCallback;
            Widget!.OfferCandidateResult = _offerCandidateResultCallback;
            Widget!.IsPickerOpen = _isPickerOpenCallback;
            Widget!.RefreshPickerRequested = _refreshPickerCallback;
            Widget!.ClosePickerRequested = _closePickerCallback;
            Widget!.PickWindowRequested = _pickWindowCallback;
        }

        /// <summary>古いウィジェットから要求口を外す。</summary>
        private static void UnbindHooks(ScreenViewWidget? oldWidget)
        {
            if (oldWidget is null) return;
            oldWidget.TogglePickerRequested = null;
            oldWidget.OfferCandidateResult = null;
            oldWidget.IsPickerOpen = null;
            oldWidget.RefreshPickerRequested = null;
            oldWidget.ClosePickerRequested = null;
            oldWidget.PickWindowRequested = null;
        }

        /// <summary>メインスレッドから呼ばれる。シートの開閉と候補の再列挙要求を行う。</summary>
        private void TogglePicker()
        {
            if (_pickerOpen)
            {
                ClosePicker();
                return;
            }

            SetState(() => _pickerOpen = true);
            RefreshCandidates();
        }

        private void ClosePicker() => SetState(() => _pickerOpen = false);

        /// <summary>候補列挙を要求する。外部要求を優先し、無いときはその場で列挙する。</summary>
        private void RefreshCandidates()
        {
            var generation = ++_candidateRequestGeneration;
            SetState(() =>
            {
                _isCandidateRefreshing = true;
                _candidateMessage = "候補を列挙しています…";
            });

            var requestRefresh = Widget!.RequestCandidateRefresh;
            if (requestRefresh is not null)
            {
                try
                {
                    requestRefresh(generation);
                }
                catch (Exception e)
                {
                    OfferCandidateResult(generation, CandidateRefreshResult.Failed(Describe(e)));
                }
                return;
            }

            try
            {
                OfferCandidateResult(generation,
                    CandidateRefreshResult.Succeeded(Widget!.CandidateEnumerator?.Invoke() ?? []));
            }
            catch (Exception e)
            {
                OfferCandidateResult(generation, CandidateRefreshResult.Failed(Describe(e)));
            }
        }

        private static string Describe(Exception exception) =>
            string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;

        /// <summary>列挙結果を状態へ反映する。</summary>
        private void OfferCandidateResult(long generation, CandidateRefreshResult result)
        {
            // シートが閉じている間の結果は保持しない(開き直し時に必ず再列挙する)。
            if (!_pickerOpen || generation != _candidateRequestGeneration) return;

            if (result.Error is { } error)
            {
                Console.Error.WriteLine($"[ScreenView] 候補の列挙に失敗しました: {error}");
                SetState(() =>
                {
                    _isCandidateRefreshing = false;
                    _candidateMessage = $"候補の列挙に失敗しました: {error}";
                });
                return;
            }

            SetState(() =>
            {
                _candidates = result.Candidates ?? [];
                _isCandidateRefreshing = false;
                _candidateMessage = null;
            });
        }

        /// <summary>行が選ばれたときの処理。対象を切り替えてシートを閉じる。</summary>
        private void PickWindow(WindowInfo window)
        {
            var switched = _session?.SwitchTarget(window.Handle) ?? false;
            if (switched)
            {
                // 切替が受け入れられたら表示のハンドルも追従させる(次回以降の既定対象)。
                _currentHandle = window.Handle;
                _message = Widget!.WaitMessage;
            }
            else
            {
                Console.Error.WriteLine($"[ScreenView] 切替先 (HWND=0x{window.Handle:X}) を受け入れられませんでした。");
            }

            ClosePicker();
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
            // ウィンドウの寸法を決める 1 枚。engine の RenderView は FixedSize
            // (WindowWidget.Size)が無いとき子へ BoxConstraints.Unbounded を渡すため、
            // オーバーレイの寸法をこのツリーで決める責任がある。
            // 宣言した寸法をそのまま採用する OverflowBox(Fit.DeferToChild)を使い、Program.cs で
            // オーバーレイに指定している解像度と揃えた寸法を渡す。中の Stack は Fit.Expand で
            // その寸法へ広がる。
            return new OverflowBox
            {
                MinWidth = OverlayWidth,
                MaxWidth = OverlayWidth,
                MinHeight = OverlayHeight,
                MaxHeight = OverlayHeight,
                Fit = OverflowBoxFit.DeferToChild,
                Alignment = Alignment.TopLeft,
                Child = new Stack { Fit = StackFit.Expand, Children = BuildStackChildren() },
            };
        }

        private List<Widget> BuildStackChildren()
        {
            // Stack は Fit.Expand で一杯に広がっているが、engine の RenderStack は Positioned の
            // Left/Right と Top/Bottom の両方が揃って初めてその寸法を割り出せるので、
            // 高さを必要とするものは両方を揃えること(Expanded は主軸の最大が有限でなければならない)。
            // 後ろに声明した子ほど前面へ描かれる。
            var children = new List<Widget>
            {
                new Positioned { Top = 0, Left = 0, Right = 0, Bottom = 0, Child = BuildCapture() },
                new Positioned { Top = 0, Left = 0, Right = 0, Height = HeaderBarHeight, Child = BuildHeaderBar() },
            };

            // シートはヘッダーバーの下から下端まで。ウィンドウ一杯のカバーを兼ねる
            // (切替直後の空白フレームを覆う)。
            if (_pickerOpen)
            {
                children.Add(new Positioned
                {
                    Top = HeaderBarHeight,
                    Left = 0,
                    Right = 0,
                    Bottom = 0,
                    Child = new WindowPickerPanel
                    {
                        Candidates = _candidates,
                        ActiveHandle = _currentHandle,
                        EmptyMessage = _candidates.Count == 0
                            ? _candidateMessage ?? "候補ウィンドウがありません。"
                            : "候補ウィンドウがありません。",
                        StatusMessage = _candidates.Count > 0 ? _candidateMessage : null,
                        IsRefreshing = _isCandidateRefreshing,
                        OnReload = Widget!.RefreshPickerRequested,
                        OnClose = Widget!.ClosePickerRequested,
                        OnPick = Widget!.PickWindowRequested,
                    },
                });
            }

            return children;
        }

        /// <summary>キャプチャ画像、または未取得のあいだの案内。</summary>
        private Widget BuildCapture()
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

        /// <summary>キャプチャ パネルの上部に常設するヘッダーバー(現在の対象名と [一覧])。</summary>
        private Widget BuildHeaderBar()
        {
            // 候補の列挙前(シート未操作時)でもヘッダーに対象名を出せるよう、
            // ショートカットの列挙はしない:分かっている名前だけ使い、分からなければアプリ名を出す。
            WindowInfo? known = _candidates.FirstOrDefault(w => w.Handle == _currentHandle);
            var label = known is { } k
                ? $"{k.ProcessName} — {WindowTargets.ElideMiddle(k.Title, HeaderTitleMaxLength)}"
                : "Screen View";

            return new GestureDetector
            {
                Behaviour = HitTestBehaviour.Opaque,
                Child = new ColoredBox
                {
                    Color = HeaderColor,
                    Child = new Padding
                    {
                        Spacing = EdgeInsets.Symmetric(vertical: 8, horizontal: 14),
                        Child = new Row
                        {
                            CrossAxisAlignment = CrossAxisAlignment.Center,
                            Children =
                            [
                                new Expanded { Child = new Text(label) { Style = HeaderStyle } },
                                new GestureDetector
                                {
                                    Behaviour = HitTestBehaviour.Opaque,
                                    OnTap = TogglePicker,
                                    Child = new ColoredBox
                                    {
                                        Color = ButtonColor,
                                        Child = new Padding
                                        {
                                            Spacing = EdgeInsets.Symmetric(vertical: 6, horizontal: 16),
                                            Child = new Text("一覧") { Style = ButtonTextStyle },
                                        },
                                    },
                                },
                            ],
                        },
                    },
                },
            };
        }

        public override void Dispose()
        {
            if (Widget is not null) UnbindHooks(Widget);
            if (_session is not null)
            {
                _session.FrameUpdated -= OnFrame;
                // 外部注入セッションは提供側の寿命で開放する。
                if (_ownedSession) _session.Dispose();
                _session = null;
            }
            base.Dispose();
        }

        private const int HeaderTitleMaxLength = 48;

        /// <summary>ヘッダーバーの高さ。シート先端の基準に使う。</summary>
        private const double HeaderBarHeight = 46;

        /// <summary>オーバーレイの既定の解像度。シート内の高さを確定させる基準に使う。</summary>
        private const double OverlayWidth = 1920;
        /// <summary><see cref="OverlayWidth"/> の高さ版。</summary>
        private const double OverlayHeight = 1080;

        private static readonly SKColor HeaderColor = SKColor.Parse("#111318");
        private static readonly SKColor ButtonColor = SKColor.Parse("#3C4453");
        private static readonly SKColor TextColor = SKColor.Parse("#E8EAED");
        private static readonly SKColor AccentColor = SKColor.Parse("#8AB4F8");

        private static readonly TextStyle HeaderStyle = new() { FontSize = 24, Color = TextColor };
        private static readonly TextStyle ButtonTextStyle = new() { FontSize = 24, Color = AccentColor, FontWeight = 700 };
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
