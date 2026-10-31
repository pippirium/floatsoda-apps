using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace FloatSoda.Apps.ScreenView;

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
    /// <param name="titleContains">タイトルの部分一致。null は指定なし(条件に加えない)。</param>
    /// <param name="processName">プロセス名(例: "chrome")。null は指定なし(条件に加えない)。</param>
    /// <param name="pid">指定した場合、該当 PID を優先します。0 は省略。</param>
    /// <remarks>
    /// 条件をひとつも渡さないと呼び出し側が意図せず全候補の先頭を返してしまうので、
    /// 例外にします(指定なしの選択は <see cref="WindowTargets.PickForeground"/> が前面の
    /// ウィンドウで行うため、通常はここへは来ません)。
    /// </remarks>
    /// <exception cref="ArgumentException">条件がひとつも指定されていない。</exception>
    public static WindowInfo? Pick(IReadOnlyList<WindowInfo> windows, string? titleContains, string? processName, uint pid = 0)
    {
        if (titleContains is null && processName is null && pid == 0)
        {
            throw new ArgumentException("titleContains / processName / pid の少なくともひとつを指定してください。", nameof(titleContains));
        }

        var candidates = windows
            .Where(w => processName is null || w.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
            .Where(w => titleContains is null || w.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
            .Where(w => pid == 0 || w.ProcessId == pid)
            .ToList();

        return candidates.Count == 0 ? null : candidates[0];
    }

    /// <summary>今前面にある(入力を受け付ける)トップレベル ウィンドウのハンドル。</summary>
    /// <remarks>
    /// 起動時の初期対象を「前面のウィンドウ」に使う。GetForegroundWindow は他のプロセスの
    /// ウィンドウを返すため、そのハンドルが <see cref="EnumerateVisibleWindows"/> の候補に
    /// 居るかは <see cref="Find"/> で突き合わせてください(タイトルを持たない窓などが混ざります)。
    /// </remarks>
    public static nint GetForegroundHandle() => PInvoke.GetForegroundWindow();

    /// <summary>ハンドルが候補に居るときだけその候補を返す。</summary>
    public static WindowInfo? Find(IReadOnlyList<WindowInfo> windows, nint handle)
    {
        if (handle == 0) return null;
        foreach (var window in windows)
        {
            if (window.Handle == handle) return window;
        }

        return null;
    }

    /// <summary>ウィンドウのクライアント領域の寸法(ピクセル)を実測します。</summary>
    /// <remarks>
    /// オーバーレイの初期比率を対象ウィンドウに合わせるために使う。ウィンドウが見つからない、
    /// または寸法を測れないときは 0x0 を返す(呼び出し側が既定値へフォールバックする)。
    /// </remarks>
    public static SKSize GetWindowClientSize(nint hwnd)
    {
        if (!GetClientRect(new HWND(hwnd), out var rect) || rect.Right - rect.Left <= 0 || rect.Bottom - rect.Top <= 0)
        {
            return SKSize.Empty;
        }

        return new SKSize(rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    /// <summary>ウィンドウのクライアント領域を取得する(user32)。取得できないときは false。</summary>
    /// <remarks>CsWin32 の <c>NativeMethods.txt</c> に矩形 API を増やさないため、ここで宣言する。</remarks>
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(HWND hWnd, out RECT lpRect);

    /// <summary><see cref="GetClientRect"/> が受け取る矩形。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static string GetWindowText(nint hwnd)
    {
        var length = PInvoke.GetWindowTextLength(new HWND(hwnd));
        if (length == 0) return string.Empty;

        // P/Invoke の戻り値はウィンドウのプロセスが応答しない/終了しかけていると
        // 0(失敗)になり得る。そのとき buffer は未初期化のまま new string(buffer) に
        // 渡すと null が返り得る(列挙側の title.Length == 0 で落とせず候補に残り、
        // 後段の表示で NullReferenceException になる)。失敗は空文字に正規化する。
        Span<char> buffer = stackalloc char[length + 1];
        var copied = PInvoke.GetWindowText(new HWND(hwnd), buffer);
        if (copied <= 0) return string.Empty;
        return new string(buffer[..copied]);
    }
}
