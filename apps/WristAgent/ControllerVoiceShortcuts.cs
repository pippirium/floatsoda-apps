using FloatSoda.Apps.ControllerInput;

namespace FloatSoda.Samples.WristAgent;

/// <summary>OpenVR初期化完了後から、腕の音声操作ショートカットをポーリングします。</summary>
public sealed class ControllerVoiceShortcuts(CodexVoiceController voice, Action<Action> post) : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly VrControllerReader _reader = new();
    private readonly ControllerShortcutState _state = new();
    private Task? _loop;
    private volatile bool _disposed;

    public void Start() => _loop ??= Task.Run(async () =>
    {
        try
        {
            while (!_cancellation.IsCancellationRequested)
            {
                var command = _state.Update(_reader.Read(), Environment.TickCount64 / 1000.0);
                if (command is { } value)
                {
                    Console.WriteLine($"[ControllerVoice] {value}");
                    post(() =>
                    {
                        if (!_disposed) _ = voice.ExecuteAsync(value switch
                        {
                            ControllerVoiceCommand.Start => CodexVoiceAction.Start,
                            ControllerVoiceCommand.Stop => CodexVoiceAction.Stop,
                            _ => CodexVoiceAction.Send,
                        });
                    });
                }
                await Task.Delay(15, _cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        catch (Exception e) { Console.Error.WriteLine($"[ControllerVoice] 入力監視を終了: {e.Message}"); }
    });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellation.Cancel();
        // OpenVR解放前に、進行中の読み取りを終える。Post先のUIは待たない。
        _loop?.GetAwaiter().GetResult();
    }
}
