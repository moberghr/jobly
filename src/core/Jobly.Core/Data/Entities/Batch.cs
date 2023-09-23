namespace Jobly.Core.Entities;

public class Batch
{
    public string Id { get; set; } = string.Empty;

    public int Counter { get; set; }

    public List<Job> Jobs { get; set; } = new();

    public Job Job { get; set; } = null!;
}