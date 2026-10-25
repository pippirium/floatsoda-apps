using FloatSoda.OVR;
using FloatSoda.OVR.Overlay;

namespace FloatSoda.Samples.WristAgent;

/// <summary>
/// SteamVR の OSK(オーバーレイ用仮想キーボード)を開いて、確定テキストを
/// <see cref="ApprovalBroker"/> へ返す小さな橋渡し。
/// </summary>
/// <remarks>
/// OSK は SteamVR で1つしか開けないため、表示専用の取り出しオーバーレイ(不可視)に
/// 紐づけて <see cref="OverlayKeyboard.TryShowForOverlay"/> を呼び、そのオーバーレイ宛の
/// <c>VREvent_KeyboardDone</c> をポーリングスレッドで受信します。
/// </remarks>
/// <param name="Broker">返答の配送先ブローカー。</param>
/// <param name="post">UI 以外の(メインスレッド以外の)スレッドからアクションをメインスレッドへ投入する処理。</param>
public sealed class WristInputService(ApprovalBroker broker, Action<Action> post)
{
    private const int PollIntervalMs = 1000 / 72;

    private OverlayIdentity? _helperIdentity;
    private OverlayEventDispatcher? _dispatcher;
    private Task? _pollLoop;
    private string? _requestId;

    /// <summary>OSK を開き、確定した入力を requestId へ返答するよう待ちます。</summary>
    /// <returns>開けた場合 true。SteamVR の OSK が他で使用中の場合 false。</returns>
    public bool TryOpen(string requestId, string? existing = null)
    {
        EnsureInitialized();

        _requestId = requestId;

        if (!OverlayKeyboard.TryShowForOverlay(_helperIdentity!.Handle, "WristAgent — 指示を入力", existing))
        {
            _requestId = null;
            return false;
        }

        return true;
    }

    private void EnsureInitialized()
    {
        if (_helperIdentity is not null) return;

        // OpenVR が初期化された後に呼ばれる前提(TryOpen の最初の呼び出し時点)。
        _helperIdentity = new OverlayIdentity("floatsoda_samples_wristagent.keyboard", "WristAgent Input");

        _dispatcher = new OverlayEventDispatcher(_helperIdentity.Handle);
        _dispatcher.Register(EVREventType.VREvent_KeyboardDone, OnKeyboardDone);
        _dispatcher.Register(EVREventType.VREvent_KeyboardClosed, OnKeyboardClosed);
        StartPollLoopIfNotStarted();
    }

    private void StartPollLoopIfNotStarted()
    {
        if (_pollLoop is not null) return;

        _pollLoop = Task.Run(() =>
        {
            while (_dispatcher is not null)
            {
                _dispatcher.PollEvents();
                Thread.Sleep(PollIntervalMs);
            }
        });
    }

    private void OnKeyboardDone(in VREvent_t _) => Submit(OverlayKeyboard.GetText());

    private void OnKeyboardClosed(in VREvent_t _)
    {
        // Done の前に閉じられた場合は決定しない(保留のまま残る)。
        _requestId = null;
    }

    private void Submit(string text)
    {
        var requestId = _requestId;
        _requestId = null;

        if (requestId is null || text.Length == 0) return;

        // OSK のポーリングスレッドで受信するため、返答の確定はメインスレッドへ投入する。
        post(() => broker.Resolve(requestId, new WristAnswer("text", text)));
    }
}
