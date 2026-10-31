using FloatSoda.Apps.ControllerInput;

namespace WristAgent.Test;

public sealed class ControllerShortcutTests
{
    private static ControllerFrame Frame(bool modifier = false, bool a = false, bool b = false, float y = 0) => new(true, modifier, a, b, y);

    [Theory]
    [InlineData(true, false, ControllerVoiceCommand.Start)]
    [InlineData(false, true, ControllerVoiceCommand.Stop)]
    [InlineData(true, true, ControllerVoiceCommand.Send)]
    public void Update_長押しで一度だけ発火し離すまで連打しない(bool a, bool b, ControllerVoiceCommand expected)
    {
        var state = new ControllerShortcutState();
        state.Update(Frame(), 0);
        Assert.Null(state.Update(Frame(true, a, b), 0.1));
        Assert.Null(state.Update(Frame(true, a, b), 0.4));
        Assert.Equal(expected, state.Update(Frame(true, a, b), 1.2));
        Assert.Null(state.Update(Frame(true, a, b), 5));
    }

    [Fact]
    public void Update_AB同時押しで開始や停止を発火しない()
    {
        var state = new ControllerShortcutState();
        state.Update(Frame(), 0);
        state.Update(Frame(true, true), 0.1);
        Assert.Null(state.Update(Frame(true, true, true), 0.2));
        Assert.Null(state.Update(Frame(true, true, true), 0.9));
        Assert.Equal(ControllerVoiceCommand.Send, state.Update(Frame(true, true, true), 1.3));
    }

    [Fact]
    public void Update_発火後にBを足しても送信しない()
    {
        var state = new ControllerShortcutState();
        state.Update(Frame(), 0);
        state.Update(Frame(true, true), 0.1);
        Assert.Equal(ControllerVoiceCommand.Start, state.Update(Frame(true, true), 0.7));
        Assert.Null(state.Update(Frame(true, true, true), 1));
        Assert.Null(state.Update(Frame(true, true, true), 3));
        state.Update(Frame(true), 4);
        state.Update(Frame(true, true, true), 4.1);
        Assert.Equal(ControllerVoiceCommand.Send, state.Update(Frame(true, true, true), 5.2));
    }

    [Fact]
    public void Update_修飾キーなしとスクロール中は音声操作しない()
    {
        var state = new ControllerShortcutState();
        state.Update(Frame(), 0);
        Assert.Null(state.Update(Frame(a: true), 1));
        Assert.Null(state.Update(Frame(a: true), 3));
        Assert.Null(state.Update(Frame(true, true, y: 1), 4));
        Assert.Null(state.Update(Frame(true, true, y: 1), 6));
    }

    [Fact]
    public void Update_切断再接続時の押しっぱなしでは発火しない()
    {
        var state = new ControllerShortcutState();
        state.Update(Frame(), 0);
        state.Update(default, 1);
        Assert.Null(state.Update(Frame(true, true, true), 2));
        Assert.Null(state.Update(Frame(true, true, true), 4));
    }

    [Fact]
    public void Scroll_修飾キー付きでだけ一定間隔に発火し方向転換は即時()
    {
        var state = new ControllerScrollState();
        Assert.Equal(0, state.Update(Frame(y: 1), 0));
        Assert.Equal(-1, state.Update(Frame(true, y: 1), 1));
        Assert.Equal(0, state.Update(Frame(true, y: 1), 1.05));
        Assert.Equal(-1, state.Update(Frame(true, y: 1), 1.2));
        Assert.Equal(1, state.Update(Frame(true, y: -1), 1.21));
        Assert.Equal(0, state.Update(Frame(true, a: true, y: -1), 2));
        Assert.Equal(0, state.Update(default, 3));
    }
}
