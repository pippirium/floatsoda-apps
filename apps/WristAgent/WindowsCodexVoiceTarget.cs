using System.Diagnostics;
using System.Windows.Automation;

namespace FloatSoda.Samples.WristAgent;

/// <summary>開いているCodexの音声入力を、名前の一致するアクセシビリティボタンで操作します。</summary>
public sealed class WindowsCodexVoiceTarget : ICodexVoiceTarget
{
    private static readonly string[] StartNames = ["音声入力", "音声入力を開始", "ディクテーション", "ディクテーションを開始", "Dictate", "Start dictation", "Use dictation", "Dictate text"];
    private static readonly string[] StopNames = ["録音を停止", "録音を終了", "音声入力を停止", "音声入力を確定", "ディクテーションを停止", "Stop dictation", "Stop recording", "Finish dictation", "Finish recording", "Confirm dictation"];
    private static readonly string[] SendNames = ["送信", "メッセージを送信", "プロンプトを送信", "Send", "Send message", "Send prompt"];

    public CodexVoiceStatus Inspect()
    {
        var window = FindWindow();
        if (window is null) return new("Codexのチャットを開いてください");
        return DescribeAvailableButtons(GetButtons(window).Select(b => b.Current.Name));
    }

    public static CodexVoiceStatus DescribeAvailableButtons(IEnumerable<string> buttonNames)
    {
        var buttons = buttonNames.Select(n => n.Trim()).ToArray();
        bool Unique(string[] names) => buttons.Count(n => names.Contains(n, StringComparer.OrdinalIgnoreCase)) == 1;
        // 重複している停止ボタンも録音状態として扱い、送信を許可しない。
        var recording = buttons.Any(n => StopNames.Contains(n, StringComparer.OrdinalIgnoreCase));
        var start = !recording && Unique(StartNames);
        var stop = recording && Unique(StopNames);
        var send = !recording && Unique(SendNames);
        return new(recording ? "録音中" : send ? "入力を確認して送信" : start ? "音声入力できます" : "音声ボタンを確認できません", start, stop, send);
    }

    public void Execute(CodexVoiceAction action)
    {
        var window = FindWindow() ?? throw new InvalidOperationException("Codexのチャットを開いてください");
        var buttons = GetButtons(window);
        var recording = buttons.Any(b => StopNames.Contains(b.Current.Name.Trim(), StringComparer.OrdinalIgnoreCase));
        if (action == CodexVoiceAction.Start && recording)
            throw new InvalidOperationException("すでに録音中です");
        if (action == CodexVoiceAction.Send && recording)
            throw new InvalidOperationException("先に録音を停止してください");
        var names = action switch { CodexVoiceAction.Start => StartNames, CodexVoiceAction.Stop => StopNames, _ => SendNames };
        var button = Find(buttons, names) ?? throw new InvalidOperationException("対象ボタンが見つからないか、現在は操作できません");
        if (!button.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
            throw new InvalidOperationException("このボタンは外部からの操作に対応していません");
        // 座標クリックやEnterの代用はしない。別アプリや生成停止ボタンへの誤操作を防ぐ。
        ((InvokePattern)pattern).Invoke();
    }

    public string[] DescribeButtons()
    {
        var window = FindWindow();
        var names = StartNames.Concat(StopNames).Concat(SendNames).Append("停止").ToArray();
        return window is null ? ["Codexのウィンドウが見つかりません"] : GetButtons(window).Select(b => b.Current.Name)
            .Where(name => names.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase)).Distinct().ToArray();
    }

    internal static AutomationElement? FindWindow()
    {
        var handles = new List<nint>();
        foreach (var name in new[] { "ChatGPT", "Codex" })
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
                if (process.MainWindowHandle != 0) handles.Add(process.MainWindowHandle);
        }
        if (handles.Count > 1) throw new InvalidOperationException("Codexのウィンドウを1つにしてください");
        return handles.Count == 1 ? AutomationElement.FromHandle(handles[0]) : null;
    }

    private static AutomationElement[] GetButtons(AutomationElement window) => window.FindAll(TreeScope.Descendants,
        new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.IsEnabledProperty, true),
            new PropertyCondition(AutomationElement.IsOffscreenProperty, false))).Cast<AutomationElement>().ToArray();

    private static AutomationElement? Find(IEnumerable<AutomationElement> buttons, string[] names)
    {
        var matches = buttons.Where(b => names.Contains(b.Current.Name.Trim(), StringComparer.OrdinalIgnoreCase)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
