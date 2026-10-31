using FloatSoda.Samples.WristAgent;

namespace WristAgent.Test;

public sealed class CodexVoiceTests
{
    [Fact]
    public void DescribeAvailableButtons_生成停止を録音停止と誤認しない()
    {
        var status = WindowsCodexVoiceTarget.DescribeAvailableButtons(["音声入力", "停止"]);
        Assert.True(status.CanStart);
        Assert.False(status.CanStop);
        Assert.False(status.CanSend);
    }

    [Fact]
    public void DescribeAvailableButtons_録音中の送信ボタンでは送信を許可しない()
    {
        var status = WindowsCodexVoiceTarget.DescribeAvailableButtons(["音声入力を停止", "送信"]);
        Assert.False(status.CanStart);
        Assert.True(status.CanStop);
        Assert.False(status.CanSend);
    }

    [Fact]
    public void DescribeAvailableButtons_録音停止後は送信を許可する()
    {
        var status = WindowsCodexVoiceTarget.DescribeAvailableButtons(["音声入力", "送信"]);
        Assert.True(status.CanStart);
        Assert.False(status.CanStop);
        Assert.True(status.CanSend);
    }

    [Fact]
    public void DescribeAvailableButtons_同名ボタンの曖昧な操作は無効()
    {
        var status = WindowsCodexVoiceTarget.DescribeAvailableButtons(["音声入力", "送信", "送信"]);
        Assert.True(status.CanStart);
        Assert.False(status.CanSend);
    }

    [Fact]
    public async Task ExecuteAsync_停止だけでは送信せず二重操作も実行しない()
    {
        var target = new BlockingTarget();
        using var controller = new CodexVoiceController(target, action => action());
        var stopping = controller.ExecuteAsync(CodexVoiceAction.Stop);
        await target.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await controller.ExecuteAsync(CodexVoiceAction.Send);
        Assert.False(controller.Status.CanSend);
        target.Release.SetResult();
        await stopping;
        Assert.Equal([CodexVoiceAction.Stop], target.Actions);
        Assert.True(controller.Status.CanSend);
    }

    [Fact]
    public async Task ExecuteAsync_外部操作の失敗を状態へ反映する()
    {
        using var controller = new CodexVoiceController(new FailingTarget(), action => action());
        await controller.ExecuteAsync(CodexVoiceAction.Start);
        Assert.Contains("操作できません", controller.Status.Message);
        Assert.False(controller.Status.CanSend);
    }

    private sealed class BlockingTarget : ICodexVoiceTarget
    {
        public List<CodexVoiceAction> Actions { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CodexVoiceStatus Inspect() => new("入力を確認して送信", CanSend: true);
        public void Execute(CodexVoiceAction action)
        {
            Actions.Add(action);
            Started.SetResult();
            Release.Task.GetAwaiter().GetResult();
        }
    }

    private sealed class FailingTarget : ICodexVoiceTarget
    {
        public CodexVoiceStatus Inspect() => new("待機中");
        public void Execute(CodexVoiceAction action) => throw new InvalidOperationException("接続なし");
    }
}
