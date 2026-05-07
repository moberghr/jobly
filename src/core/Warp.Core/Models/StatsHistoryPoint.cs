namespace Warp.Core.Models;

public class StatsHistoryPoint
{
    public DateTime Hour { get; set; }

    public long Succeeded { get; set; }

    public long Failed { get; set; }

    public long Requeued { get; set; }
}
