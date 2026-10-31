namespace FloatSoda.Apps.ScreenView;

/// <summary>
/// キャプチャ対象の候補を列挙し、除外ルールを共有するヘルパー。
/// </summary>
/// <remarks>
/// <para>
/// 起動時の初期対象選択と、ウィンドウ ピッカーの両方から呼ぶ。除外ルールをここに一本化し、
/// 呼び出し側で条件が diverge しないようにする。
/// </para>
/// <para>
/// <see cref="WindowLocator.EnumerateVisibleWindows"/> は呼び出しごとにプロセスを開くため、
/// <b>頻繁な呼び出しは想定外</b>。ピッカーではシートを開いた時点と [再読込] のときだけ呼ぶ。
/// </para>
/// </remarks>
public static class WindowTargets
{
    /// <summary>シェル由来で対象にしても意味の無いウィンドウのプロセス名。</summary>
    private static readonly string[] ExcludedProcessNames = ["explorer", "TextInputHost", "SearchHost", "StartMenuExperienceHost"];

    /// <summary>自分自身のオーバーレイ ウィンドウを候補に入れないためのプロセスID。</summary>
    private static readonly uint SelfProcessId = (uint)Environment.ProcessId;

    /// <summary>除外対象かどうか。候補の列挙と前面ウィンドウの選択で同じ規則を使う。</summary>
    public static bool IsExcluded(WindowInfo window)
        => window.ProcessId == SelfProcessId || ExcludedProcessNames.Contains(window.ProcessName, StringComparer.OrdinalIgnoreCase);

    /// <summary>除外を適用した候補ウィンドウを列挙します。</summary>
    /// <remarks>呼び出し元のスレッドから Win32 を直接呼ぶ。バックグラウンド スレッドから呼ぶこと。</remarks>
    public static IReadOnlyList<WindowInfo> EnumerateCandidates()
    {
        return WindowLocator
            .EnumerateVisibleWindows()
            .Where(w => !IsExcluded(w))
            .ToList();
    }

    /// <summary>
    /// 起動時の初期対象として「前面のウィンドウ」を選ぶ。
    /// </summary>
    /// <remarks>
    /// 前面の窓が候補に居ない(除外対象のシェル由来、またはタイトルを持たない窓)ときは
    /// <paramref name="candidates"/> の先頭へ退避する。どちらも無ければ null。
    /// <paramref name="candidates"/> は除外を適用済みの候補列挙の結果を渡すこと。
    /// </remarks>
    public static WindowInfo? PickForeground(IReadOnlyList<WindowInfo> candidates)
    {
        // 前面の窓が除外対象(シェル由来など)なら、そこから選び直さず候補の先頭へ退避する。
        return WindowLocator.Find(candidates, WindowLocator.GetForegroundHandle())
            ?? (candidates.Count == 0 ? null : candidates[0]);
    }

    /// <summary>候補をプロセス名(アルファベット順)→ タイトル順のグループへまとめる。</summary>
    public static IReadOnlyList<WindowTargetGroup> GroupByProcess(IReadOnlyList<WindowInfo> candidates)
    {
        return candidates
            .GroupBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            // タイトルは取得失敗で null になり得るため、キーを空文字に正規化して比較する
            // (StringComparer.Ordinal は null を渡すと NullReferenceException を投げる)。
            .Select(g => new WindowTargetGroup(g.Key, [.. g.OrderBy(w => w.Title ?? string.Empty, StringComparer.Ordinal)]))
            .ToList();
    }

    /// <summary>
    /// タイトルを中央省略で短縮する。
    /// </summary>
    /// <remarks>
    /// 先頭だけ切るのは「同じ前置きで始まる窓」の判別が効かないため、中点を置いて前後を残す。
    /// </remarks>
    /// <remarks>
    /// <paramref name="text" /> が null の場合は空文字として扱う。タイトルは Win32 由来で、
    /// ウィンドウのプロセスが応答しない/終了しかけていると取得に失敗して null になり得る。
    /// ここで落ちるとウィンドウ一覧の描画ごとアプリが止まるため、表示側では弾かない。
    /// </remarks>
    public static string ElideMiddle(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.Length <= maxLength) return text;
        if (maxLength <= 1) return "…"[..Math.Max(maxLength, 0)];

        // 中点 1 文字を挟む前後の長さを決める(末尾に少し多く残す)。
        var body = maxLength - 1;
        var head = body / 2;
        var tail = body - head;
        return string.Concat(text.AsSpan(0, head), "…", text.AsSpan(text.Length - tail));
    }
}

/// <summary>ウィンドウ ピッカーの表示単位。プロセス名とそのグループに属する候補。</summary>
/// <param name="ProcessName">プロセス名(グループ ヘッダーに使う)。</param>
/// <param name="Windows">このプロセスの候補ウィンドウ(タイトル順)。</param>
public readonly record struct WindowTargetGroup(string ProcessName, IReadOnlyList<WindowInfo> Windows);
