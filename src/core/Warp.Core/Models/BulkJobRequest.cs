namespace Warp.Core.Models;

public class BulkJobRequest
{
    public Guid[] JobIds { get; set; } = [];
}
