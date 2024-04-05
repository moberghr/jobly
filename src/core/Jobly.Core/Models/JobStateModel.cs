using Jobly.Core.Entities;
using Jobly.Core.Enums;

namespace Jobly.Core.Models;
public class JobStateModel
{
    public Guid Id { get; set; }

    public State State { get; set; }

    public DateTime DateTime { get; set; }

    public string? Message { get; set; }

    public Guid JobId { get; set; }

    public Job Job { get; set; }
}
