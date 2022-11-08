namespace Handfire.Core.Entities;

public class OutboxMessage
{
    public int Id { get; set; }

    public string Type { get; set; }

    public string Message { get; set; }

    public DateTime CreateTime { get; set; }

    public DateTime? ProcessedTime { get; set; }
}
