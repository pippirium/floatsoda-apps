using FloatSoda.Elements;
using FloatSoda.Gesture;
using FloatSoda.Geometrics;
using FloatSoda.Painting;
using FloatSoda.Widgets;
using FloatSoda.Widgets.Gesture;
using FloatSoda.Widgets.Layout;
using FloatSoda.Widgets.Paint;
using SkiaSharp;

namespace FloatSoda.Samples.WristAgent;

/// <summary>腕ディスプレイに表示する要求カード。保留中の要求と最近の履歴を表示します。</summary>
/// <param name="Broker">要求ブローカー。表示内容の観察元です。</param>
/// <param name="DebugLog">デバッグ時のログ表示。null の場合はログ画面を非表示にします。</param>
/// <param name="Input">SteamVR OSK によるフリーテキスト入力。null の場合は[入力]ボタンが非表示になります。</param>
public record WristAgentCard(ApprovalBroker Broker, WristInputService? Input = null, WristDebugLog? DebugLog = null) : StatefulWidget<WristAgentCard>
{
    public override State<WristAgentCard> CreateState() => new WristAgentCardState();
}

public class WristAgentCardState : State<WristAgentCard>
{
    private static readonly SKColor BackgroundColor = SKColor.Parse("#111318");
    private static readonly SKColor CardColor = SKColor.Parse("#1E232B");
    private static readonly SKColor AllowColor = SKColor.Parse("#2E7D32");
    private static readonly SKColor DenyColor = SKColor.Parse("#B3261E");
    private static readonly SKColor NeutralColor = SKColor.Parse("#3C4453");
    private static readonly SKColor TextColor = SKColor.Parse("#E8EAED");
    private static readonly SKColor SubTextColor = SKColor.Parse("#9AA0A6");
    private static readonly SKColor AccentColor = SKColor.Parse("#8AB4F8");

    private bool _showLogs;
    private int _logPage;

    public override void InitState()
    {
        // 変化通知は必ずメインスレッドから発火する(ApprovalBroker の契約)ため、そのまま SetState してよい。
        Widget!.Broker.Changed += OnBrokerChanged;
        if (Widget.DebugLog is { } log) log.Changed += OnBrokerChanged;
    }

    public override void Dispose()
    {
        Widget?.Broker.Changed -= OnBrokerChanged;
        if (Widget?.DebugLog is { } log) log.Changed -= OnBrokerChanged;
        base.Dispose();
    }

    private void OnBrokerChanged() => SetState(() => { });

    public override Widget Build(IBuildContext context)
    {
        var broker = Widget!.Broker;
        var pending = broker.Pending;
        var history = broker.History;

        var children = new List<Widget> { BuildHeader(pending.Count == 0 ? "待機中" : $"要確認 ×{pending.Count}") };

        if (Widget.DebugLog is not null)
        {
            children.Add(new Row
            {
                Children =
                [
                    BuildButton("要求", NeutralColor, () => SetState(() => _showLogs = false)),
                    new SizedBox { Width = 8 },
                    BuildButton("ログ", NeutralColor, () => SetState(() => _showLogs = true)),
                ],
            });
        }

        if (_showLogs && Widget.DebugLog is { } debugLog)
        {
            BuildLogs(children, debugLog);
        }
        else
        {
            foreach (var request in pending)
            {
                children.Add(BuildRequestCard(request));
            }

            if (pending.Count == 0)
            {
                children.Add(Space(new Text("待機中 — エージェントの要求が届くとここにカードが出ます") { Style = Sub(15) }, 14));
            }

            if (history.Count > 0)
            {
                children.Add(Space(new Text("最近の返答") { Style = Sub(15) }, 10));
            }

            foreach (var (request, answer) in history)
            {
                children.Add(Space(new Text($"[{answer.Decision}] {Describe(request)}") { Style = Sub(13, SubTextColor) }, 3));
            }
        }

        return new ClipRoundRect
        {
            BorderRadius = BorderRadius.All(Radius.Circular(24)),
            Child = new ColoredBox
            {
                Color = BackgroundColor,
                Child = new SizedBox
                {
                    Width = 640,
                    Height = 900,
                    Child = new Padding
                    {
                        Spacing = EdgeInsets.All(20),
                        Child = new Column
                        {
                            MainAxisAlignment = MainAxisAlignment.Start,
                            Children = children,
                        },
                    },
                },
            },
        };
    }

    private void BuildLogs(List<Widget> children, WristDebugLog log)
    {
        const int pageSize = 6;
        var entries = log.Entries;
        var pages = Math.Max(1, (entries.Length + pageSize - 1) / pageSize);
        _logPage = Math.Clamp(_logPage, 0, pages - 1);
        children.Add(Space(new Text($"Debug ログ {_logPage + 1}/{pages}（新しい順・最大100件）") { Style = Sub(15) }, 12));
        children.Add(new Row
        {
            Children =
            [
                BuildButton("最新", NeutralColor, () => SetState(() => _logPage = 0)),
                new SizedBox { Width = 8 },
                BuildButton("◀", NeutralColor, () => SetState(() => _logPage = Math.Max(0, _logPage - 1))),
                new SizedBox { Width = 8 },
                BuildButton("▶", NeutralColor, () => SetState(() => _logPage = Math.Min(pages - 1, _logPage + 1))),
            ],
        });
        foreach (var entry in entries.Skip(_logPage * pageSize).Take(pageSize))
        {
            // 長い行はカードをはみ出さない範囲に省略する。完全なログはコンソールで確認できる。
            var text = entry.Length > 160 ? entry[..160] + "…" : entry;
            children.Add(Space(new Text(text) { Style = Sub(14) }, 12));
        }
        if (entries.Length == 0)
            children.Add(Space(new Text("ログはまだありません") { Style = Sub(15) }, 12));
    }

    private static Widget BuildHeader(string status) => new Padding
    {
        Spacing = EdgeInsets.Symmetric(vertical: 6),
        Child = new Row
        {
            Children =
            [
                new Text("WristAgent") { Style = Sub(26, AccentColor) },
                new SizedBox { Width = 12 },
                new Spacer(),
                new Text(status) { Style = Sub(20) },
            ],
        },
    };

    private Widget BuildRequestCard(WristRequest request) => new Padding
    {
        Spacing = EdgeInsets.Symmetric(vertical: 8),
        Child = new ColoredBox
        {
            Color = CardColor,
            Child = new Padding
            {
                Spacing = EdgeInsets.All(14),
                Child = new Column
                {
                    MainAxisAlignment = MainAxisAlignment.Start,
                    Children =
                    [
                        new Text($"{KindLabel(request.Kind)} · {request.Agent}") { Style = Sub(18, AccentColor) },
                        Space(new Text(Describe(request)) { Style = Sub(15) }, 6),
                        Space(BuildButtonRow(request), 10),
                    ],
                },
            },
        },
    };

    private Widget BuildButtonRow(WristRequest request) => new Row
    {
        Children =
        [
            BuildButton("はい", AllowColor, () => Widget!.Broker.Resolve(request.Id, new WristAnswer("allow", null))),
            new SizedBox { Width = 8 },
            BuildButton("いいえ", DenyColor, () => Widget!.Broker.Resolve(request.Id, new WristAnswer("deny", null))),
            new SizedBox { Width = 8 },
            BuildButton("続行", NeutralColor, () => Widget!.Broker.Resolve(request.Id, new WristAnswer("continue", null))),
            new SizedBox { Width = 8 },
            ..(Widget?.Input is not null
                ? new Widget[] { BuildButton("入力", NeutralColor, () => Widget?.Input?.TryOpen(request.Id)) }
                : Array.Empty<Widget>()),
        ],
    };

    private static Widget BuildButton(string label, SKColor color, Action onTap) => new GestureDetector
    {
        OnTap = onTap,
        Behaviour = HitTestBehaviour.Opaque,
        Child = new ColoredBox
        {
            Color = color,
            Child = new Padding
            {
                Spacing = EdgeInsets.Symmetric(vertical: 14, horizontal: 24),
                Child = new Center { Child = new Text(label) { Style = new TextStyle { Color = SKColors.White, FontSize = 20 } } },
            },
        },
    };

    /// <summary>子ウィジェットの下へ固定間隔を入れるためのパディング。</summary>
    private static Padding Space(Widget child, double height) => new()
    {
        Spacing = new EdgeInsets(0, height, 0, 0),
        Child = child,
    };

    private static string Describe(WristRequest request)
    {
        var prefix = string.IsNullOrEmpty(request.Tool) ? "" : $"{request.Tool}: ";
        var detail = request.Detail.Length > 240 ? request.Detail[..240] + "…" : request.Detail;
        return prefix + detail;
    }

    private static string KindLabel(string kind) => kind switch
    {
        "approve" => "承認要求",
        "continue" => "継続要求",
        "question" => "指示入力",
        "notify" => "通知",
        _ => kind,
    };

    private static TextStyle Sub(double size, SKColor? color = null) => new()
    {
        Color = color ?? TextColor,
        FontSize = size,
    };
}
