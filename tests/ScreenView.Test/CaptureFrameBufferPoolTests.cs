using FloatSoda.Apps.ScreenView;

namespace ScreenView.Test;

public sealed class CaptureFrameBufferPoolTests
{
    [Fact]
    public void Rent_必要なサイズちょうどの配列を再利用する()
    {
        var pool = new CaptureFrameBufferPool();
        var buffer = pool.Rent(10001);
        Assert.Equal(10001, buffer.Length);
        pool.Return(buffer);
        Assert.Same(buffer, pool.Rent(10001));
    }

    [Fact]
    public void Return_空き配列を一枚より多く保持しない()
    {
        var pool = new CaptureFrameBufferPool();
        var first = pool.Rent(1024);
        var second = pool.Rent(1024);
        pool.Return(first);
        pool.Return(second);
        Assert.Same(first, pool.Rent(1024));
        Assert.NotSame(second, pool.Rent(1024));
    }

    [Fact]
    public void Rent_サイズ変更後に古い大容量配列を保持しない()
    {
        var pool = new CaptureFrameBufferPool();
        var large = pool.Rent(1024 * 1024);
        pool.Return(large);
        var small = pool.Rent(256);
        pool.Return(large);
        pool.Return(small);
        Assert.Same(small, pool.Rent(256));
        Assert.NotSame(large, pool.Rent(1024 * 1024));
    }

    [Fact]
    public void Clear_返却済み配列も未返却の古い配列も保持しない()
    {
        var pool = new CaptureFrameBufferPool();
        var first = pool.Rent(1024);
        pool.Return(first);
        pool.Clear();
        pool.Return(first);
        Assert.NotSame(first, pool.Rent(1024));
    }

    [Fact]
    public void Rent_繰り返し利用で大容量の割り当てを増やさない()
    {
        var pool = new CaptureFrameBufferPool();
        const int size = 8 * 1024 * 1024;
        pool.Return(pool.Rent(size));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) pool.Return(pool.Rent(size));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1024);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rent_不正なサイズを拒否する(int size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureFrameBufferPool().Rent(size));
}
