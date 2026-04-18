using Jobly.Core.Logging;
using Shouldly;

namespace Jobly.Tests.Unit;

[Trait("Category", "NoDb")]
public class JobLogCollectorTests
{
    [TimedFact]
    public void Drain_ReturnsAllEntries_AndClearsQueue()
    {
        var collector = new JobLogCollector { JobId = Guid.NewGuid() };
        collector.Add("Information", "msg1");
        collector.Add("Warning", "msg2");

        var entries = collector.Drain();

        entries.Count.ShouldBe(2);
        entries[0].Message.ShouldBe("msg1");
        entries[1].Message.ShouldBe("msg2");

        // Second drain should be empty
        collector.Drain().Count.ShouldBe(0);
    }

    [TimedFact]
    public void Drain_ReturnsEmptyList_WhenQueueIsEmpty()
    {
        var collector = new JobLogCollector { JobId = Guid.NewGuid() };

        var entries = collector.Drain();

        entries.ShouldBeEmpty();
    }

    [TimedFact]
    public async Task Drain_DoesNotLoseEntries_WhenAddAndDrainConcurrently()
    {
        var collector = new JobLogCollector { JobId = Guid.NewGuid() };
        const int totalEntries = 1000;
        var allDrained = new List<Core.Data.Entities.JobLog>();

        // Producer: add entries from multiple threads
        var producerTask = Task.Run(async () =>
        {
            var tasks = Enumerable.Range(0, totalEntries).Select(i =>
                Task.Run(() => collector.Add("Information", $"msg-{i}")));
            await Task.WhenAll(tasks);
        });

        // Consumer: drain periodically while producer is running
        var producerDone = false;
        var consumerTask = Task.Run(async () =>
        {
            while (!producerDone)
            {
                allDrained.AddRange(collector.Drain());
                await Task.Delay(1);
            }

            // Final drain to catch any remaining entries
            allDrained.AddRange(collector.Drain());
        });

        await producerTask;
        producerDone = true;
        await consumerTask;

        // All entries should be accounted for — none lost
        allDrained.Count.ShouldBe(totalEntries);
    }
}
