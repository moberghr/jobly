namespace Warp.Core.Models;

public class CounterHistoryPoint
{
    public DateTime Hour { get; set; }

    public string Key { get; set; } = string.Empty;

    public long Value { get; set; }
}
