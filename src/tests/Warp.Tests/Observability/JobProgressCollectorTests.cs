using Shouldly;
using Warp.Core.Logging;

namespace Warp.Tests.Observability;

[Trait("Category", "NoDb")]
public class JobProgressCollectorTests
{
    [TimedFact]
    public void Drain_NewBar_EmitsOneRow()
    {
        var collector = new JobProgressCollector { JobId = Guid.NewGuid() };
        collector.Report("download", 42);

        var rows = collector.Drain();

        rows.Count.ShouldBe(1);
        rows[0].Name.ShouldBe("download");
        rows[0].Value.ShouldBe((short)42);
        rows[0].EventType.ShouldBe("Progress");
    }

    [TimedFact]
    public void Drain_SameValueTwice_EmitsOneRowThenNone()
    {
        var collector = new JobProgressCollector { JobId = Guid.NewGuid() };
        collector.Report("download", 50);
        var first = collector.Drain();

        collector.Report("download", 50);
        var second = collector.Drain();

        first.Count.ShouldBe(1);
        second.ShouldBeEmpty();
    }

    [TimedFact]
    public void Drain_ChangedValue_EmitsRow()
    {
        var collector = new JobProgressCollector { JobId = Guid.NewGuid() };
        collector.Report("download", 50);
        collector.Drain();

        collector.Report("download", 75);
        var rows = collector.Drain();

        rows.Count.ShouldBe(1);
        rows[0].Value.ShouldBe((short)75);
    }

    [TimedFact]
    public void Drain_MultipleNamedBars_EmitsOneRowPerBar()
    {
        var collector = new JobProgressCollector { JobId = Guid.NewGuid() };
        collector.Report("download", 30);
        collector.Report("process", 10);

        var rows = collector.Drain();

        rows.Count.ShouldBe(2);
        rows.ShouldContain(x => x.Name == "download" && x.Value == 30);
        rows.ShouldContain(x => x.Name == "process" && x.Value == 10);
    }

    [TimedFact]
    public void Drain_NoReports_ReturnsEmpty()
    {
        var collector = new JobProgressCollector { JobId = Guid.NewGuid() };

        collector.Drain().ShouldBeEmpty();
    }

    [TimedFact]
    public void Drain_OnlyChangedBars_EmitsRow_OthersSkipped()
    {
        var collector = new JobProgressCollector { JobId = Guid.NewGuid() };
        collector.Report("download", 50);
        collector.Report("process", 10);
        collector.Drain();

        collector.Report("download", 50);
        collector.Report("process", 20);
        var rows = collector.Drain();

        rows.Count.ShouldBe(1);
        rows[0].Name.ShouldBe("process");
        rows[0].Value.ShouldBe((short)20);
    }

    [TimedFact]
    public void Drain_PopulatesJobIdAndWorkerId()
    {
        var jobId = Guid.NewGuid();
        var workerId = Guid.NewGuid();
        var collector = new JobProgressCollector { JobId = jobId, WorkerId = workerId };
        collector.Report("x", 5);

        var rows = collector.Drain();

        rows[0].JobId.ShouldBe(jobId);
        rows[0].WorkerId.ShouldBe(workerId);
    }
}
