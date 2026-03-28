namespace Jobly.Core;

public class JoblyException : Exception
{
    public JoblyException(string message)
        : base(message)
    {
    }

    public JoblyException()
        : base()
    {
    }

    public JoblyException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
