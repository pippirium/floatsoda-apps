using Microsoft.Extensions.Logging;

namespace FloatSoda.Samples.WristAgent;

/// <summary>デバッグ画面用に最新100件のログをメモリ上で保持するロガープロバイダー。</summary>
public sealed class WristDebugLog : ILoggerProvider
{
    private readonly object _sync = new();
    private readonly Queue<string> _entries = new();
    private Action<Action>? _post;
    private bool _notificationPending;
    private bool _disposed;

    /// <summary>ログが更新されたことをメインスレッドへ通知します。</summary>
    public event Action? Changed;

    /// <summary>新しい順にログのスナップショットを取得します。</summary>
    public string[] Entries
    {
        get { lock (_sync) return _entries.Reverse().ToArray(); }
    }

    /// <summary>画面更新をメインスレッドへ送る処理を設定します。</summary>
    public void SetDispatcher(Action<Action> post)
    {
        lock (_sync) _post = post;
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new DebugLogger(this, categoryName);

    private void Append(LogLevel level, string category, string message)
    {
        Action<Action>? post;
        lock (_sync)
        {
            if (_disposed) return;
            // 1件のサイズも制限する。改行を除き、固定サイズのカードでページ表示する。
            message = message.Replace('\r', ' ').Replace('\n', ' ');
            if (message.Length > 500) message = message[..500] + "…";
            _entries.Enqueue($"{DateTimeOffset.Now:HH:mm:ss} [{level}] {category.Split('.').Last()}: {message}");
            while (_entries.Count > 100) _entries.Dequeue();
            post = _post;
            if (post is null || _notificationPending) return;
            _notificationPending = true;
        }

        post(() =>
        {
            lock (_sync)
            {
                _notificationPending = false;
                if (_disposed) return;
            }
            Changed?.Invoke();
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _post = null;
            Changed = null;
            _entries.Clear();
        }
    }

    private sealed class DebugLogger(WristDebugLog owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug && logLevel < LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            if (exception is not null) message += $" ({exception.GetType().Name}: {exception.Message})";
            owner.Append(logLevel, category, message);
        }
    }
}
