namespace Jobly.Worker.Interceptors;

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