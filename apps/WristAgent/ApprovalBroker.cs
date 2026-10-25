using System.Collections.Concurrent;

namespace FloatSoda.Samples.WristAgent;

/// <summary>エージェント(Hooks/MCP)から腕 UI へ届く1件の要求。</summary>
/// <param name="Id">要求の識別子。HTTP 側で発生させます。</param>
/// <param name="Kind">要求種別。approve(承認) / continue(継続) / question(自由入力) / notify(通知のみ)。</param>
/// <param name="Agent">送信元エージェント名（例: codex / claude-code）。</param>
/// <param name="Tool">承認対象のツール名（例: Bash / apply_patch）。question では空。</param>
/// <param name="Detail">本体。承認なら実行コマンドや差分の要約、question なら質問文。</param>
public readonly record struct WristRequest(
    string Id,
    string Kind,
    string Agent,
    string Tool,
    string Detail);

/// <summary>腕 UI からユーザーが返した1件の返答。</summary>
/// <param name="Decision">allow / deny / text / timeout。</param>
/// <param name="Value">Decision が text のときの入力テキスト。それ以外は null。</param>
public readonly record struct WristAnswer(string Decision, string? Value);

/// <summary>
/// 腕 UI と HTTP サーバーの間で要求と返答を仲介するブローカー。
/// </summary>
/// <remarks>
/// <see cref="AskAsync"/> は任意スレッドから呼べます。要求は内部キューに入り、
/// メインスレッドから <see cref="DrainIncoming"/> を呼ぶことで UI へ順次現れます。
/// ユーザーが腕 UI で選択すると対応する <see cref="AskAsync"/> の Task が完了します。
/// </remarks>
public sealed class ApprovalBroker
{
    private readonly ConcurrentQueue<(WristRequest Request, TaskCompletionSource<WristAnswer> Completion)> _incoming = new();
    private readonly List<(WristRequest Request, TaskCompletionSource<WristAnswer> Completion)> _pending = [];
    private readonly Queue<(WristRequest Request, WristAnswer Answer)> _history = new();
    private readonly Action<Action>? _post;

    /// <summary>ブローカーを生成します。</summary>
    /// <param name="post">任意スレッドからメインスレッドへアクションを投入する処理（<see cref="FloatSodaApp.Post"/>）。</param>
    public ApprovalBroker(Action<Action>? post = null)
    {
        _post = post;
    }

    /// <summary>保留・履歴が変化したことを UI へ通知するイベント。</summary>
    /// <remarks>必ずメインスレッドから発火します（<see cref="DrainIncoming"/> / <see cref="Resolve"/> / <see cref="ResolveAll"/> からのみ）。</remarks>
    public event Action? Changed;

    /// <summary>現在保留中の要求一覧を取得します。先頭が最も古い項目です。</summary>
    public IReadOnlyList<WristRequest> Pending => _pending.Select(p => p.Request).ToArray();

    /// <summary>最近の返答履歴を取得します。新しいものが後ろです。</summary>
    public IReadOnlyList<(WristRequest Request, WristAnswer Answer)> History => _history.ToArray();

    /// <summary>要求を投げ、ユーザーの返答を待ちます。HTTP サーバーから呼びます。</summary>
    public Task<WristAnswer> AskAsync(WristRequest request)
    {
        var completion = new TaskCompletionSource<WristAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        _incoming.Enqueue((request, completion));

        // 新着要求の UI 反映はメインスレッドで行う。外部スレッドからは投入のみ。
        _post?.Invoke(DrainIncoming);

        return completion.Task;
    }

    /// <summary>メインスレッドから呼び、未処理の新着要求を UI へ反映します。</summary>
    public void DrainIncoming()
    {
        var added = false;
        while (_incoming.TryDequeue(out var item))
        {
            _pending.Add(item);
            added = true;
        }

        if (added)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>ユーザーの返答を保留中の要求へ返します。メインスレッドから呼びます。</summary>
    /// <param name="id">要求の <see cref="WristRequest.Id"/>。</param>
    /// <returns>対応する要求が存在した場合 true。</returns>
    public bool Resolve(string id, WristAnswer answer)
    {
        var index = _pending.FindIndex(p => p.Request.Id == id);
        if (index < 0) return false;

        var (request, completion) = _pending[index];
        _pending.RemoveAt(index);
        Remember(request, answer);
        Changed?.Invoke();
        completion.TrySetResult(answer);
        return true;
    }

    /// <summary>すべての保留を指定した返答で強制完了します。メインスレッドから呼びます。</summary>
    public void ResolveAll(WristAnswer answer)
    {
        if (_pending.Count == 0) return;

        foreach (var (request, completion) in _pending)
        {
            Remember(request, answer);
            completion.TrySetResult(answer);
        }

        _pending.Clear();
        Changed?.Invoke();
    }

    private void Remember(WristRequest request, WristAnswer answer)
    {
        _history.Enqueue((request, answer));
        while (_history.Count > 5)
        {
            _history.Dequeue();
        }
    }
}
