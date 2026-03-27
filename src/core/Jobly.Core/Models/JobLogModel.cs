namespace Jobly.Core.Models;

public class JobLogModel
{
    public Guid Id { get; set; }

    public DateTime Timestamp { get; set; }

    public string Level { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? Exception { get; set; }
}
