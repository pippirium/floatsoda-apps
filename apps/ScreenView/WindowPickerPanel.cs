using FloatSoda.Elements;
using FloatSoda.Gesture;
using FloatSoda.Geometrics;
using FloatSoda.Painting;
using FloatSoda.Widgets;
using FloatSoda.Widgets.Gesture;
using FloatSoda.Widgets.Layout;
using FloatSoda.Widgets.Paint;
using SkiaSharp;

namespace FloatSoda.Apps.ScreenView;

/// <summary>
/// キャプチャ対象を選ぶオーバーレイ上のシート。プロセス名でグループ化した一覧を改ページで表示します。
/// </summary>
/// <remarks>
/// <para>
/// <c>ListView</c> / <c>GridView</c> がまだ internal のため、スクロールではなく
/// <b>グループ分け + 改ページ</b>で一覧を実現します。1 ページ 8 行(グループ名は行として数えない)。
/// 編集中の窓が入ったページは Build のたびに追跡し、候補の差し替えでそのページへ寄せます。
/// </para>
/// <para>
/// 候補の列挙は <paramref name="OnReload"/> で親へ要求し、結果を <see cref="Candidates"/> として
/// 受け取ります(本ウィジェットは Win32 を直接呼びません)。
/// </para>
/// </remarks>
public record WindowPickerPanel : StatefulWidget<WindowPickerPanel>
{
    /// <summary>一覧に表示する候補。</summary>
    public required IReadOnlyList<WindowInfo> Candidates { get; init; }

    /// <summary>現在キャプチャ中の対象。null は未確定。</summary>
    public nint ActiveHandle { get; init; }

    /// <summary>候補の再列挙を親へ要求する。</summary>
    public Action? OnReload { get; init; }

    /// <summary>シートを閉じる要求を親へ伝える。</summary>
    public Action? OnClose { get; init; }

    /// <summary>行が選ばれたときの処理。引数は選ばれたウィンドウ。</summary>
    public Action<WindowInfo>? OnPick { get; init; }

    /// <summary>候補が無いときに表示する文章。</summary>
    public string EmptyMessage { get; init; } = "候補ウィンドウがありません。";

    /// <summary>候補を保持したまま再読込中または失敗したときに表示する状態文。</summary>
    public string? StatusMessage { get; init; }

    /// <summary>候補を再読込中かどうか。</summary>
    public bool IsRefreshing { get; init; }

    /// <inheritdoc />
    public override State<WindowPickerPanel> CreateState() => new WindowPickerPanelState();

    private sealed class WindowPickerPanelState : State<WindowPickerPanel>
    {
        /// <summary>1 ページの行数。グループ名は行として数えない。</summary>
        private const int RowsPerPage = 8;

        private int _page;

        /// <summary>直近の Build で編集中の窓が入ったページ。0 の見直し判定だけに使う。</summary>
        private int _activePage;

        public override Widget Build(IBuildContext context)
        {
            var groups = WindowTargets.GroupByProcess(Widget!.Candidates);
            var pages = BuildPages(groups);
            if (pages.Count == 0)
            {
                _page = 0;
            }
            else if (_page != _activePage && _activePage < pages.Count)
            {
                // 編集中の窓が居るページへ寄せる。利用者がページをめくったらそのまま残す。
                // (候補の列挙と対象切替で Widget.Candidates が差し替わったときに効く。)
                _page = _activePage;
            }
            else if (_page >= pages.Count)
            {
                // 候補の差し替えで页数が減ったら、最後尾より先には居られない。
                _page = pages.Count - 1;
            }

            var body = pages.Count == 0
                ? [BuildEmptyRow()]
                : pages[_page];

            // シートの寸法をここで決める。オーバーレイの寸法がエンジン側で決まらない
            // つくり(WorldSpaceWindow は WindowWidget.Size 未指定で使う)では、engine の
            // RenderStack が Positioned 子(4辺指定)へ渡す制約が無限のままになり、横幅を
            // 宣言しないと行のテキストが折り返して縦に伸びる。
            // (engine の RenderFlex は flex 子を持つとき主軸の最大が無限だと例外を投げるので、
            //  呼び出し側は高さを必ず有限にすること。)
            // シート地は OverflowBox で寸法を決めた箱に塗る。engine の RenderProxyBox と
            // RenderPadding は受け取った制約の上限まで広がるため、ConstrainedBox や Container の
            // 中で背景を塗るとシート地がオーバーレイの幅まで広がる。engine の RenderConstrainedBox
            // (SizedBox)は子の寸法をそのまま採用するので、背景より内側の箱では寸法を決められない。
            // OverflowBox は DeferToChild で子の寸法を自身の寸法にし、内側へ渡す制約はここで
            // 宣言した幅と高さになるため、シート地が本体の外側へはみ出さない。
            return new OverflowBox
            {
                MinWidth = SheetWidth,
                MaxWidth = SheetWidth,
                MinHeight = SheetHeight,
                MaxHeight = SheetHeight,
                Fit = OverflowBoxFit.DeferToChild,
                Alignment = Alignment.TopLeft,
                Child = new ColoredBox
                {
                    Color = SheetColor,
                    Child = new Padding
                    {
                        Spacing = EdgeInsets.All(Pad),
                        // 本体はシートから余白を引いた幅を宣言する。engine の RenderPadding は
                        // 余白を引いた制約を子へ渡すが上限がそのまま残るため、ここで宣言して本体を
                        // シートの左に付ける。宣言しないと呼び出し側の制約しだいで本体が中央へ寄る。
                        Child = new SizedBox
                        {
                            Width = ContentWidth,
                            Height = SheetBodyHeight,
                            Child = new Flex
                            {
                                Direction = Axis.Vertical,
                                Children =
                                [
                                    new SizedBox
                                    {
                                        Width = ContentWidth,
                                        Height = SheetHeaderHeight,
                                        Child = BuildSheetHeader(),
                                    },
                                    new SizedBox
                                    {
                                        Width = ContentWidth,
                                        Height = PageHeight,
                                        Child = new Flex { Direction = Axis.Vertical, MainAxisSize = MainAxisSize.Max, Children = body },
                                    },
                                    new SizedBox
                                    {
                                        Width = ContentWidth,
                                        Height = PageFootHeight,
                                        Child = BuildPagerRow(_page, pages.Count),
                                    },
                                ],
                            },
                        },
                    },
                },
            };
        }

        /// <summary>グループを行単位でページへ流し込む。グループ名は行として数えない。</summary>
        private List<List<Widget>> BuildPages(IReadOnlyList<WindowTargetGroup> groups)
        {
            var activeHandle = Widget!.ActiveHandle;
            var pages = new List<List<Widget>>();
            var current = new List<Widget>();
            var rows = 0;
            var activePage = 0;

            void Flush()
            {
                if (current.Count == 0) return;
                pages.Add(current);
                current = [];
                rows = 0;
            }

            // 次のページへ移る。ページをまたいでもグループ名は必ず先頭に添える。
            void NextPage(WindowTargetGroup group)
            {
                Flush();
                current.Add(BuildGroupHeader(group));
            }

            // 行と行を 1px のシート地で区切る。engine の RenderFlex は子と子の間に余白を
            // 開けるプロパティを持たないので、仕切り用の Widget を挟む。
            void AddDivider()
            {
                if (current.Count > 0) current.Add(Divider());
            }

            foreach (var group in groups)
            {
                // 次のページの先頭でもグループ名を添えるので、ページを割ってから足す。
                if (rows == RowsPerPage) NextPage(group);

                AddDivider();
                current.Add(BuildGroupHeader(group));
                foreach (var window in group.Windows)
                {
                    if (rows == RowsPerPage) NextPage(group);

                    AddDivider();
                    current.Add(BuildRow(window, activeHandle));
                    rows++;
                    if (window.Handle == activeHandle) activePage = pages.Count;
                }
            }

            Flush();
            _activePage = activePage;
            return pages;
        }

        private Widget BuildSheetHeader()
        {
            var titleChildren = new List<Widget>
            {
                new Text("キャプチャ対象") { Style = HeaderStyle },
            };

            if (Widget!.StatusMessage is { } statusMessage)
            {
                titleChildren.Add(new Text(WindowTargets.ElideMiddle(statusMessage, StatusMessageMaxLength))
                {
                    Style = StatusStyle,
                });
            }

            return new Flex
            {
                Direction = Axis.Horizontal,
                CrossAxisAlignment = CrossAxisAlignment.Center,
                Children =
                [
                    new Expanded
                    {
                        Child = new Flex
                        {
                            Direction = Axis.Vertical,
                            MainAxisSize = MainAxisSize.Min,
                            Children = titleChildren,
                        },
                    },
                    BuildButton("閉じる", NeutralColor, () => Widget!.OnClose?.Invoke()),
                ],
            };
        }

        /// <summary>グループ ヘッダー。</summary>
        private static Widget BuildGroupHeader(WindowTargetGroup group) => new Container
        {
            // engine の RenderFlex は子の寸法から自身の幅と交差軸の配置を逆算するため、
            // 行と仕切りと同じ幅を宣言して揃える。
            Width = ContentWidth,
            // engine の RenderPadding は余白を引いた制約を子に渡すだけで、自身の寸法は
            // 子の寸法 + 余白に戻る。余白のぶんの高さだけ先に確保しておかないと、シートが
            // ウィンドウ一杯の箱になったときにこの見出しが本体の行地と重なる。
            Height = GroupHeaderHeight,
            Padding = EdgeInsets.Symmetric(vertical: Gap, horizontal: 12),
            Child = new Text($"{group.ProcessName} ({group.Windows.Count} 窓)") { Style = GroupStyle },
        };

        private Widget BuildRow(WindowInfo window, nint activeHandle)
        {
            var isActive = window.Handle == activeHandle;
            var label = isActive
                ? $"● {WindowTargets.ElideMiddle(window.Title, TitleMaxLength)}"
                : $"   {WindowTargets.ElideMiddle(window.Title, TitleMaxLength)}";

            return new GestureDetector
            {
                Behaviour = HitTestBehaviour.Opaque,
                OnTap = () => Widget!.OnPick?.Invoke(window),
                // Container に幅と高さを宣言して行の地の寸法を決める。engine の RenderPadding は
                // 子の寸法まで縮み、engine の RenderFlex は子の寸法から自身の寸法と交差軸の配置を
                // 逆算するため、寸法を宣言しないと行の位置がテキスト長に応じてずれる。
                // 高さを宣言すると engine の RenderPadding はテキストを残りの高さの中央へ置く。
                // (一覧部の高さは PageHeight で宣言するので、候補の行数がページぶんより少なく
                //  なってもページ制御の位置は変わらない。)
                Child = new Container
                {
                    Color = isActive ? ActiveColor : RowColor,
                    Padding = EdgeInsets.Symmetric(vertical: 10, horizontal: 12),
                    Width = ContentWidth,
                    // 行の高さも宣言する。engine の RenderPadding は余白を引いた制約を子に
                    // 渡すだけで自身の寸法は子のぶんまで戻るため、宣言しないとテキストのぶんの
                    // 高さになり、行と仕切りの合計がページの高さを超えてページ制御と重なる。
                    Height = RowHeight,
                    Child = new Text(label) { Style = isActive ? ActiveTextStyle : RowStyle },
                },
            };
        }

        /// <summary>行と行の仕切り。シート地が 1px 見えるだけの細い箱。</summary>
        private static Widget Divider() => new Container
        {
            // 幅を宣言しないと engine の RenderFlex が逆算するシート幅が仕切りの 0 幅になる。
            Width = ContentWidth,
            Height = DividerThickness,
            Color = DividerColor,
        };

        private Widget BuildEmptyRow() => new Container
        {
            Color = RowColor,
            Alignment = Alignment.Center,
            Width = ContentWidth,
            Height = PageHeight,
            Padding = EdgeInsets.Symmetric(vertical: 10, horizontal: 12),
            Child = new Text(Widget!.EmptyMessage) { Style = SubStyle },
        };

        private Widget BuildPagerRow(int page, int pageCount) => new Flex
        {
            // ページ制御は呼び出し側の Container が幅と高さを宣言して受け取る。
            Direction = Axis.Horizontal,
            MainAxisAlignment = MainAxisAlignment.Center,
            CrossAxisAlignment = CrossAxisAlignment.Center,
            Children =
            [
                BuildButton("◀", NeutralColor, () => GoPage(page - 1),
                    Widget!.IsRefreshing || pageCount == 0 || page <= 0),
                new Padding
                {
                    Spacing = EdgeInsets.Symmetric(horizontal: 14),
                    Child = new Text(pageCount == 0 ? "–" : $"{page + 1}/{pageCount}") { Style = RowStyle },
                },
                BuildButton("▶", NeutralColor, () => GoPage(page + 1),
                    Widget!.IsRefreshing || pageCount == 0 || page >= pageCount - 1),
                new SizedBox { Width = 14 },
                BuildButton("再読込", NeutralColor, () => Widget!.OnReload?.Invoke(), Widget!.IsRefreshing),
            ],
        };

        private void GoPage(int target) => SetState(() => _page = Math.Max(target, 0));

        private static Widget BuildButton(string label, SKColor color, Action onTap, bool disabled = false)
        {
            return new GestureDetector
            {
                Behaviour = HitTestBehaviour.Opaque,
                // 無効時は HitTest を通すが何も起こさない(位置がずれない見た目にする)。
                OnTap = disabled ? null : onTap,
                Child = new Opacity
                {
                    Value = disabled ? 0.35 : 1.0,
                    Child = new ColoredBox
                    {
                        Color = color,
                        Child = new Padding
                        {
                            Spacing = EdgeInsets.Symmetric(vertical: 8, horizontal: 14),
                            Child = new Text(label) { Style = ButtonStyle },
                        },
                    },
                },
            };
        }


        // ---- 寸法・書式(Dpm 4000 相当での実物視認性は HMD で要確認) ----

        private const double Pad = 18;
        private const double Gap = 10;
        private const double SheetWidth = 980;

        /// <summary>シート内の本体が使える幅。行・グループ ヘッダー・仕切りはこの幅を宣言して揃える。</summary>
        /// <remarks>engine の RenderFlex は子の寸法から自身の幅を逆算するため、揃えたいものは同じ幅を宣言する。</remarks>
        private const double ContentWidth = SheetWidth - Pad * 2;
        private const int TitleMaxLength = 60;
        private const int StatusMessageMaxLength = 72;

        /// <summary>ページ制御を収める高さ。</summary>
        private const double PageFootHeight = 58;

        /// <summary>
        /// 一覧部の高さ。ページ 1 ぶんの高さをここで決める。engine の RenderPadding は
        /// 子の寸法まで縮み、engine の RenderFlex は MainAxisSize.Max でも子の合計まで
        /// 縮むため、ここで宣言しないと候補の行数で一覧部の高さが変わりページ制御が動く。
        /// </summary>
        private const double PageHeight = 8 * (RowHeight + DividerThickness) + GroupHeaderHeight + DividerThickness;

        /// <summary>シート全体の高さ。上下の余白と一覧部、ページ制御を収める。</summary>
        private const double SheetHeight = SheetBodyHeight + Pad * 2;

        /// <summary>シート地から余白を引いた本体の高さ。見出し・一覧・ページ制御の合計。</summary>
        private const double SheetBodyHeight = PageHeight + SheetHeaderHeight + PageFootHeight;

        /// <summary>シート見出し(「キャプチャ対象」と「閉じる」)の高さ。</summary>
        private const double SheetHeaderHeight = 58;

        /// <summary>仕切りの厚み。engine の RenderFlex は子と子の間に余白を開けないので、仕切り用の箱で区切る。</summary>
        private const double DividerThickness = 1;

        /// <summary>行の地の高さ(テキスト 24 + 上下の余白 10 × 2)。宣言して行の寸法を決める。</summary>
        private const double RowHeight = 44;

        /// <summary>グループ ヘッダーの高さ(グループ名 22 + 上下の余白 10 × 2)。宣言して寸法を決める。</summary>
        private const double GroupHeaderHeight = 48;

        private static readonly SKColor SheetColor = SKColor.Parse("#111318");
        private static readonly SKColor RowColor = SKColor.Parse("#1E232B");
        private static readonly SKColor ActiveColor = SKColor.Parse("#2A3A55");
        private static readonly SKColor NeutralColor = SKColor.Parse("#3C4453");
        private static readonly SKColor DividerColor = SKColor.Parse("#111318");
        private static readonly SKColor TextColor = SKColor.Parse("#E8EAED");
        private static readonly SKColor SubTextColor = SKColor.Parse("#9AA0A6");
        private static readonly SKColor AccentColor = SKColor.Parse("#8AB4F8");

        private static readonly TextStyle HeaderStyle = new() { FontSize = 30, Color = TextColor, FontWeight = 700 };
        private static readonly TextStyle GroupStyle = new() { FontSize = 22, Color = AccentColor, FontWeight = 700 };
        private static readonly TextStyle RowStyle = new() { FontSize = 24, Color = TextColor };
        private static readonly TextStyle ActiveTextStyle = new() { FontSize = 24, Color = AccentColor, FontWeight = 700 };
        private static readonly TextStyle SubStyle = new() { FontSize = 24, Color = SubTextColor };
        private static readonly TextStyle StatusStyle = new() { FontSize = 16, Color = SubTextColor };
        private static readonly TextStyle ButtonStyle = new() { FontSize = 24, Color = TextColor };
    }
}
