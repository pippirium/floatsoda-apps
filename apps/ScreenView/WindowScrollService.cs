using System.Windows;
using System.Windows.Automation;

namespace FloatSoda.Apps.ScreenView;

/// <summary>キャプチャ対象のスクロール領域を、フォーカスを奪わずに操作します。</summary>
public sealed class WindowScrollService(Func<nint> currentWindow) : IDisposable
{
    private int _busy;
    private volatile bool _disposed;
    private Task? _pending;
    private long _lastWarning;

    public void Request(int direction)
    {
        if (_disposed || direction == 0 || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        var handle = currentWindow();
        _pending = Task.Run(() =>
        {
            try
            {
                if (_disposed || handle == 0 || currentWindow() != handle) return;
                var root = AutomationElement.FromHandle(handle);
                var rootBounds = root.Current.BoundingRectangle;
                var point = new Point(rootBounds.X + rootBounds.Width * 0.65, rootBounds.Y + rootBounds.Height * 0.5);
                var candidates = FindCandidates(root).OrderByDescending(e => e.Current.BoundingRectangle.Contains(point))
                    .ThenByDescending(e => e.Current.BoundingRectangle.Width * e.Current.BoundingRectangle.Height);
                var target = candidates.FirstOrDefault() ?? throw new InvalidOperationException("対象ウィンドウにスクロール可能な領域が見つかりません");
                if (_disposed || currentWindow() != handle) return;
                ((ScrollPattern)target.GetCurrentPattern(ScrollPattern.Pattern)).Scroll(ScrollAmount.NoAmount,
                    direction < 0 ? ScrollAmount.SmallDecrement : ScrollAmount.SmallIncrement);
            }
            catch (Exception e)
            {
                var now = Environment.TickCount64;
                if (now - _lastWarning > 5000)
                {
                    _lastWarning = now;
                    Console.Error.WriteLine($"[ScreenView Scroll] {e.Message}");
                }
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        });
    }

    public static string[] Describe(nint handle) => FindCandidates(AutomationElement.FromHandle(handle))
        .Select(e => $"{e.Current.ControlType.ProgrammaticName}: {e.Current.BoundingRectangle}").ToArray();

    private static IEnumerable<AutomationElement> FindCandidates(AutomationElement root) => root.FindAll(TreeScope.Subtree,
        new AndCondition(new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true),
            new PropertyCondition(AutomationElement.IsOffscreenProperty, false)))
        .Cast<AutomationElement>().Where(e => e.Current.IsEnabled && !e.Current.BoundingRectangle.IsEmpty
            && e.TryGetCurrentPattern(ScrollPattern.Pattern, out var pattern) && ((ScrollPattern)pattern).Current.VerticallyScrollable);

    public void Dispose()
    {
        _disposed = true;
        _pending?.GetAwaiter().GetResult();
    }
}
