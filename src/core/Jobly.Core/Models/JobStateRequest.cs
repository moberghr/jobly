namespace Jobly.Core.Models;
public class JobStateRequest : BaseListRequest
{
    public Guid JobId { get; set; }
}
