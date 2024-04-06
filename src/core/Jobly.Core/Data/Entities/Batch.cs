namespace Jobly.Core.Entities;

public class Batch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public DateTime CreatedTime { get; set; } = DateTime.UtcNow;

    public int Counter { get; set; }

    public List<Job> Jobs { get; set; } = new();
    
}