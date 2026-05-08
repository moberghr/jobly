namespace Warp.Core.Data.Entities;

public class ConcurrencyLimit
{
    public string Name { get; set; } = string.Empty;

    public int Limit { get; set; }

    public DateTime UpdatedAt { get; set; }
}
