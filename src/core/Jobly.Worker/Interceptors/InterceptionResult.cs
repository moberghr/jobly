namespace Jobly.Worker.Interceptors;

/// <summary>
/// This class is based on the <see cref="InterceptionResult"/> class from the <see cref="Microsoft.EntityFrameworkCore"/> namespace.
/// And is very much in progress.
/// </summary>
public readonly struct InterceptionResult
{
    /// <summary>
    /// Suppresses the execution of the job.
    /// </summary>
    public static InterceptionResult Suppress() => new InterceptionResult(true);

    private InterceptionResult(bool suppress) => this.IsSuppressed = suppress;

    /// <summary>If true, then interception is suppressed.</summary>
    public bool IsSuppressed { get; }
}