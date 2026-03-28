namespace Jobly.Core.Entities;

public enum BatchContinuationOptions
{
    /// <summary>
    /// Continuation fires only when all jobs in the batch succeed.
    /// If any job fails, the continuation remains in Awaiting state.
    /// </summary>
    OnlyOnSucceeded = 0,

    /// <summary>
    /// Continuation fires when all jobs in the batch reach a terminal state (Completed or Failed).
    /// </summary>
    OnAnyFinishedState = 1,
}

public class Batch
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int Counter { get; set; }

    public BatchContinuationOptions ContinuationOptions { get; set; } = BatchContinuationOptions.OnlyOnSucceeded;

    public List<Job> Jobs { get; set; } = new();
}