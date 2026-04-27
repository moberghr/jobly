namespace Warp.Core;

public class WarpException : Exception
{
    public WarpException(string message)
        : base(message)
    {
    }

    public WarpException()
        : base()
    {
    }

    public WarpException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
