using System.Windows;
using System.Windows.Automation;

namespace FloatSoda.Samples.WristAgent;

/// <summary>ScreenViewのキャプチャ先とは独立して、Codexモードのチャットだけをスクロールします。</summary>
public sealed class CodexChatScrollService : IDisposable
{
    private int _busy;
    private volatile bool _disposed;
    private Task? _pending;
    private long _lastWarning;

    public static bool IsCodexModeLabel(string name) => name.Contains("Codex", StringComparison.OrdinalIgnoreCase)
        && (name.StartsWith("モード切り替え", StringComparison.Ordinal)
            || name.StartsWith("Mode switcher", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Switch mode", StringComparison.OrdinalIgnoreCase));

    public void Request(int direction)
    {
        if (_disposed || direction == 0 || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        _pending = Task.Run(() =>
        {
            try
            {
                if (_disposed) return;
                var root = WindowsCodexVoiceTarget.FindWindow()
                    ?? throw new InvalidOperationException("Codexのチャットを開いてください");
                var mode = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
                    .Cast<AutomationElement>().Any(e => !e.Current.IsOffscreen && IsCodexModeLabel(e.Current.Name));
                if (!mode) throw new InvalidOperationException("Codexモードのチャットを開いてください");
                var bounds = root.Current.BoundingRectangle;
                var point = new Point(bounds.X + bounds.Width * 0.65, bounds.Y + bounds.Height * 0.5);
                // サイドバーではなくチャット側にある大きいスクロール領域だけを使う。
                var target = root.FindAll(TreeScope.Subtree,
                    new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true))
                    .Cast<AutomationElement>().Where(e => !e.Current.IsOffscreen && e.Current.IsEnabled
                        && e.Current.BoundingRectangle.Width > 50 && e.Current.BoundingRectangle.Height > 50
                        && e.Current.BoundingRectangle.Contains(point)
                        && ((ScrollPattern)e.GetCurrentPattern(ScrollPattern.Pattern)).Current.VerticallyScrollable)
                    .OrderByDescending(e => e.Current.BoundingRectangle.Width * e.Current.BoundingRectangle.Height).FirstOrDefault()
                    ?? throw new InvalidOperationException("Codexのチャットのスクロール領域を確認できません");
                if (_disposed) return;
                ((ScrollPattern)target.GetCurrentPattern(ScrollPattern.Pattern)).Scroll(ScrollAmount.NoAmount,
                    direction < 0 ? ScrollAmount.SmallDecrement : ScrollAmount.SmallIncrement);
            }
            catch (Exception e)
            {
                var now = Environment.TickCount64;
                if (now - _lastWarning > 5000)
                {
                    _lastWarning = now;
                    Console.Error.WriteLine($"[Codex Scroll] {e.Message}");
                }
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        });
    }

    public void Dispose()
    {
        _disposed = true;
        _pending?.GetAwaiter().GetResult();
    }
}
