using System.ComponentModel.DataAnnotations;
using Handfire.Core.Entities;
using Handfire.Core.Enums;
using Handfire.Core.Interfaces;

namespace Handfire.Core.Data.Entities;

public class Batch : IConcurrencyToken
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public State BatchStatus { get; set; }

    public int Counter { get; set; }

    public List<BatchContinuation> BatchContinuations { get; set; } = new();

    public List<Job> Jobs { get; set; } = new();

    [ConcurrencyCheck]
    public Guid Version { get; set; }
}