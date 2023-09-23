namespace Jobly.Test.Shared.Entities;

public class EmailLog
{
    public int Id { get; set; }

    public string Email { get; set; }

    public string Subject { get; set; }

    public string Body { get; set; }

    public DateTime? ProcessedTime { get; set; }
}
