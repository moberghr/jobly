using Shouldly;
using Warp.Core.Handlers;
using Warp.Core.Logging;

namespace Warp.Tests.Observability;

[Trait("Category", "NoDb")]
public class JobContextProgressTests
{
    [TimedFact]
    public void ReportProgress_WithoutCollector_DoesNotThrow()
    {
        var ctx = new JobContext { JobId = Guid.NewGuid() };

        Should.NotThrow(() => ctx.ReportProgress("download", 50));
        Should.NotThrow(() => ctx.ReportProgress(75));
    }

    [TimedFact]
    public void ReportProgress_WithCollector_WritesToCollector()
    {
        var collector = new JobProgressCollector { JobId = Guid.NewGuid() };
        var ctx = new JobContext { JobId = Guid.NewGuid(), ProgressCollector = collector };

        ctx.ReportProgress("download", 42);

        var rows = collector.Drain();
        rows.Count.ShouldBe(1);
        rows[0].Name.ShouldBe("download");
        rows[0].Value.ShouldBe((short)42);
    }

    [TimedFact]
    public void ReportProgress_NoName_UsesEmptyString()
    {
        var collector = new JobProgressCollector { JobId = Guid.NewGuid() };
        var ctx = new JobContext { JobId = Guid.NewGuid(), ProgressCollector = collector };

        ctx.ReportProgress(60);

        var rows = collector.Drain();
        rows[0].Name.ShouldBe(string.Empty);
        rows[0].Value.ShouldBe((short)60);
    }

    [TimedFact]
    public void ReportProgress_NegativePercent_ClampsToZero()
    {
        var collector = new JobProgressCollector { JobId = Guid.NewGuid() };
        var ctx = new JobContext { JobId = Guid.NewGuid(), ProgressCollector = collector };

        ctx.ReportProgress("x", -10);

        var rows = collector.Drain();
        rows[0].Value.ShouldBe((short)0);
    }

    [TimedFact]
    public void ReportProgress_OverHundred_ClampsToOneHundred()
    {
        var collector = new JobProgressCollector { JobId = Guid.NewGuid() };
        var ctx = new JobContext { JobId = Guid.NewGuid(), ProgressCollector = collector };

        ctx.ReportProgress("x", 250);

        var rows = collector.Drain();
        rows[0].Value.ShouldBe((short)100);
    }

    [TimedFact]
    public void ReportProgress_NullName_TreatedAsEmptyString()
    {
        var collector = new JobProgressCollector { JobId = Guid.NewGuid() };
        var ctx = new JobContext { JobId = Guid.NewGuid(), ProgressCollector = collector };

        ctx.ReportProgress(null!, 50);

        var rows = collector.Drain();
        rows[0].Name.ShouldBe(string.Empty);
    }
}
