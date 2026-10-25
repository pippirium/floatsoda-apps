using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace FloatSoda.Samples.ChromeView;

/// <summary>キャプチャ対象候補になるトップレベルウィンドウの情報。</summary>
public readonly record struct WindowInfo(nint Handle, string Title, string ProcessName, uint ProcessId);

/// <summary>表示中のトップレベルウィンドウを列挙・検索します。</summary>
public static class WindowLocator
{
    /// <summary>可視かつタイトルを持つトップレベルウィンドウを列挙します。</summary>
    /// <remarks>呼び出しごとにプロセスを参照するため、頻繁な呼び出しは想定外です(起動時の候補探索用)。</remarks>
    public static IReadOnlyList<WindowInfo> EnumerateVisibleWindows()
    {
        var windows = new List<nint>();
        PInvoke.EnumWindows(
            (hWnd, _) =>
            {
                if (PInvoke.IsWindowVisible(hWnd)) windows.Add((nint)hWnd);
                return true;
            },
            default);

        var processNames = new Dictionary<uint, string>();
        var result = new List<WindowInfo>(windows.Count);
        foreach (var hwnd in windows)
        {
            var title = GetWindowText(hwnd);
            if (title.Length == 0) continue;

            PInvoke.GetWindowThreadProcessId((HWND)hwnd, out var pid);
            if (pid == 0) continue;

            if (!processNames.TryGetValue(pid, out var processName))
            {
                try
                {
                    using var process = Process.GetProcessById((int)pid);
                    processName = process.ProcessName;
                }
                catch (ArgumentException)
                {
                    // 列挙との間にプロセスが終了した。このウィンドウは既に存在しない。
                    continue;
                }
                processNames[pid] = processName;
            }

            result.Add(new WindowInfo(hwnd, title, processName, pid));
        }

        return result;
    }

    /// <summary>条件に合うキャプチャ対象を選びます。</summary>
    /// <param name="titleContains">タイトルの部分一致。null ならプロセス名のみで判定します。</param>
    /// <param name="processName">対象のプロセス名(例: "chrome")。</param>
    /// <param name="pid">指定した場合、該当 PID を優先します。0 は省略。</param>
    public static WindowInfo? Pick(IReadOnlyList<WindowInfo> windows, string? titleContains, string processName, uint pid = 0)
    {
        var candidates = windows
            .Where(w => w.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
            .Where(w => titleContains is null || w.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
            .Where(w => pid == 0 || w.ProcessId == pid)
            .ToList();

        return candidates.Count == 0 ? null : candidates[0];
    }

    private static string GetWindowText(nint hwnd)
    {
        var length = PInvoke.GetWindowTextLength(new HWND(hwnd));
        if (length == 0) return string.Empty;

        Span<char> buffer = stackalloc char[length + 1];
        var copied = PInvoke.GetWindowText(new HWND(hwnd), buffer);
        return new string(buffer[..copied]);
    }
}
