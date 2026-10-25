using System.Runtime.InteropServices;

namespace FloatSoda.Samples.WristAgent;

/// <summary>
/// Windows のグローバルホットキー(Ctrl+Alt+Y/N/C/T)をローレベルキーボードフックで受ける保険。
/// フォーカス状態に依存せず、キーは握り潰して元のアプリへ渡しません。
/// </summary>
/// <remarks>フックのメッセージポンプを専用スレッドで回します。開始は <see cref="Start"/>。</remarks>
public sealed class GlobalHotkeys
{
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmSyskeydown = 0x0104;
    private const int VkLControl = 0xA2;
    private const int VkRControl = 0xA3;
    private const int VkLMenu = 0xA4;
    private const int VkRMenu = 0xA5;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern void UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll")]
    private static extern int GetMessage(nint lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    private readonly IReadOnlyDictionary<int, Action> _actions;
    private readonly Action<Action> _post;
    private Thread? _pumpThread;

    /// <summary>ホットキー待ち受けを構成します。</summary>
    /// <param name="actions">仮想キーコード → 実行する処理(メインスレッドへ投入する前段)のマップ。</param>
    /// <param name="post">フックスレッドからメインスレッドへアクションを投入する処理。フック内では UI 触らない。</param>
    public GlobalHotkeys(IReadOnlyDictionary<int, Action> actions, Action<Action> post)
    {
        _actions = actions;
        _post = post;
    }

    /// <summary>専用スレッドでメッセージポンプとキーボードフックを開始します。</summary>
    public void Start()
    {
        _pumpThread = new Thread(Pump) { IsBackground = true, Name = "WristAgentHotkeys" };
        _pumpThread.Start();
    }

    private void Pump()
    {
        LowLevelKeyboardProc proc = HookCallback;
        var hook = SetWindowsHookEx(WhKeyboardLl, proc, nint.Zero, 0);
        if (hook == nint.Zero)
        {
            Console.WriteLine("グローバルホットキーのフック登録に失敗しました(ホットキーは無効)");
            return;
        }

        // GetMessage ループ。フックコールバックはこのスレッドのメッセージ処理で呼ばれる。
        while (GetMessage(nint.Zero, nint.Zero, 0, 0) > 0)
        {
        }

        UnhookWindowsHookEx(hook);
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && (wParam == WmKeydown || wParam == WmSyskeydown))
        {
            var vk = Marshal.ReadByte(lParam);
            if (_actions.ContainsKey(vk) && IsModifierPressed())
            {
                _post(_actions[vk]);
                // テキスト入力运转中のアプリへキーが漏れるのを防ぐため握り潰す。
                return 1;
            }
        }

        return CallNextHookEx(nint.Zero, nCode, wParam, lParam);
    }

    private static bool IsModifierPressed() =>
        IsAnyDown(VkLControl, VkRControl) && IsAnyDown(VkLMenu, VkRMenu);

    private static bool IsAnyDown(params int[] keys) => keys.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0);
}
