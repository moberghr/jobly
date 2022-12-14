using Handfire.Core.Entities;
using Handfire.Core.Enums;

namespace Handfire.Core.Models;
public class JobStateModel
{
    public int Id { get; set; }

    public State State { get; set; }

    public DateTime DateTime { get; set; }

    public string? Message { get; set; }

    public int JobId { get; set; }

    public Job Job { get; set; }
}
