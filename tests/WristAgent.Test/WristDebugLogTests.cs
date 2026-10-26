using System.Collections.Concurrent;
using FloatSoda.Samples.WristAgent;
using Microsoft.Extensions.Logging;
using Xunit;

namespace WristAgent.Test;

public class WristDebugLogTests
{
    [Fact]
    public void RetainsLatestEntriesAndBoundsIndividualMessages()
    {
        using var log = new WristDebugLog();
        var logger = log.CreateLogger("test");
        for (var i = 0; i < 105; i++) logger.LogDebug("Entry {Index}", i);
        Assert.Equal(100, log.Entries.Length);
        Assert.EndsWith("Entry 104", log.Entries[0]);
        Assert.EndsWith("Entry 5", log.Entries[^1]);
        logger.LogDebug("{Message}", new string('x', 1000) + "\nend");
        Assert.True(log.Entries[0].Length < 550);
        Assert.DoesNotContain('\n', log.Entries[0]);
    }

    [Fact]
    public void ConcurrentLogsCoalesceNotificationsAndDispatchToUi()
    {
        using var log = new WristDebugLog();
        var queued = new ConcurrentQueue<Action>();
        log.SetDispatcher(queued.Enqueue);
        var notifications = 0;
        log.Changed += () => notifications++;
        var logger = log.CreateLogger("test");
        Parallel.For(0, 1000, i => logger.LogDebug("Entry {Index}", i));
        Assert.Equal(100, log.Entries.Length);
        Assert.Equal(0, notifications);
        Assert.Single(queued);
        Assert.True(queued.TryDequeue(out var notify));
        notify();
        Assert.Equal(1, notifications);
        logger.LogWarning("Next batch");
        Assert.Single(queued);
    }

    [Fact]
    public void DisposeSuppressesQueuedNotificationsAndFurtherWrites()
    {
        var log = new WristDebugLog();
        var queued = new Queue<Action>();
        log.SetDispatcher(queued.Enqueue);
        var notifications = 0;
        log.Changed += () => notifications++;
        var logger = log.CreateLogger("test");
        logger.LogDebug("Before disposal");
        log.Dispose();
        queued.Dequeue()();
        logger.LogWarning("After disposal");
        Assert.Empty(log.Entries);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task BrokerLogsLifecycleWithoutRequestOrAnswerText()
    {
        using var log = new WristDebugLog();
        using var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug).AddProvider(log));
        var queued = new Queue<Action>();
        var broker = new ApprovalBroker(queued.Enqueue, factory.CreateLogger<ApprovalBroker>());
        var answerTask = broker.AskAsync(new WristRequest("private-id", "question", "private-agent", "private-tool", "private-detail"));
        Assert.False(answerTask.IsCompleted);
        queued.Dequeue()();
        Assert.Single(broker.Pending);
        Assert.True(broker.Resolve("private-id", new WristAnswer("text", "private-answer")));
        Assert.Equal("private-answer", (await answerTask).Value);
        Assert.Empty(broker.Pending);
        Assert.Contains(log.Entries, e => e.Contains("要求を受信"));
        Assert.Contains(log.Entries, e => e.Contains("要求へ返答"));
        Assert.All(log.Entries, e => Assert.DoesNotContain("private-", e));
    }
}
