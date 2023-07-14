using Handfire.Core.Entities;

namespace Handfire.Core.Data.Entities;

public class BatchContinuation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public Job Job { get; set; } = null!;

    public string JobId { get; set; } = null!;

    public Batch Batch { get; set; } = null!;

    public string BatchId { get; set; } = null!;
}
