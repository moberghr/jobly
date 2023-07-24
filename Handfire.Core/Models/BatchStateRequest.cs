namespace Handfire.Core.Models;

public class BatchStateRequest : BaseListRequest
{
    public string BatchId { get; set; } = string.Empty;
}