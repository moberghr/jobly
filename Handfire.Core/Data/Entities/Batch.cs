using Handfire.Core.Entities;
using Handfire.Core.Enums;

namespace Handfire.Core.Data.Entities;

public class Batch
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public State BatchStatus { get; set; }

    public int Counter { get; set; }

    public List<Job> Jobs { get; set; } = new();

    public string JobId { get; set; } = null!;
}