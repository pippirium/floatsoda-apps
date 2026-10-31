namespace FloatSoda.Samples.WristAgent;

public enum CodexVoiceAction { Start, Stop, Send }

public sealed record CodexVoiceStatus(string Message, bool CanStart = false, bool CanStop = false, bool CanSend = false);

public interface ICodexVoiceTarget
{
    CodexVoiceStatus Inspect();
    void Execute(CodexVoiceAction action);
}

/// <summary>音声操作を直列化し、状態通知を腕UIのメインスレッドへ戻します。</summary>
public sealed class CodexVoiceController(ICodexVoiceTarget target, Action<Action> post) : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _busy;
    private volatile bool _disposed;
    private Task? _polling;
    public CodexVoiceStatus Status { get; private set; } = new("Codexを確認中…");
    public event Action? Changed;

    public void StartMonitoring() => _polling ??= PollAsync();

    public async Task ExecuteAsync(CodexVoiceAction action)
    {
        if (_disposed || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        Publish(new("操作中…"));
        await _gate.WaitAsync();
        try
        {
            var result = await Task.Run(() =>
            {
                // キャッシュした状態で操作せず、実行直前のUIを毎回確認する。
                target.Execute(action);
                return target.Inspect();
            });
            Publish(result);
        }
        catch (Exception e)
        {
            Publish(new($"操作できません: {e.Message}"));
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            _gate.Release();
        }
    }

    private async Task PollAsync()
    {
        try
        {
            while (!_cancellation.IsCancellationRequested)
            {
                if (Volatile.Read(ref _busy) == 0 && await _gate.WaitAsync(0))
                {
                    try { Publish(await Task.Run(target.Inspect)); }
                    catch (Exception e) { Publish(new($"Codexを確認できません: {e.Message}")); }
                    finally { _gate.Release(); }
                }
                await Task.Delay(1200, _cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
    }

    private void Publish(CodexVoiceStatus status) => post(() =>
    {
        if (_disposed || Status == status) return;
        Status = status;
        Changed?.Invoke();
    });

    public void Dispose()
    {
        _disposed = true;
        _cancellation.Cancel();
        // UI Automationの実行中はセマフォを破棄しない。
    }
}
