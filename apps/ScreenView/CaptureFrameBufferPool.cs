namespace FloatSoda.Apps.ScreenView;

/// <summary>
/// キャプチャ専用の配列再利用。フレームリングとは別に空き配列を1枚だけ保持し、
/// スレッドごとの共有プールに大きい画像配列を残しません。
/// </summary>
public sealed class CaptureFrameBufferPool
{
    private readonly object _sync = new();
    private byte[]? _cached;
    private int _size;

    public byte[] Rent(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        lock (_sync)
        {
            if (_size != length)
            {
                _cached = null;
                _size = length;
            }
            if (_cached is { } cached)
            {
                _cached = null;
                return cached;
            }
        }
        return GC.AllocateUninitializedArray<byte>(length);
    }

    public void Return(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        lock (_sync)
        {
            if (_cached is null && buffer.Length == _size) _cached = buffer;
        }
    }

    public void Clear()
    {
        lock (_sync) { _cached = null; _size = 0; }
    }
}
