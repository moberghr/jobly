using Jobly.Core.Entities;
using Jobly.Core.Enums;

namespace Jobly.Core.Models;
public class JobStateModel
{
    public int Id { get; set; }

    public State State { get; set; }

    public DateTime DateTime { get; set; }

    public string? Message { get; set; }

    public string JobId { get; set; }

    public Job Job { get; set; }
}
