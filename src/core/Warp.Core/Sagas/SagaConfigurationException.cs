namespace Warp.Core.Sagas;

/// <summary>
/// Thrown when the saga subsystem detects a misconfiguration that cannot be recovered from at
/// runtime — typically a missing or mistyped <see cref="CorrelateAttribute"/>, or a handler
/// registration that doesn't implement <c>ISagaHandler&lt;,&gt;</c>.
/// </summary>
public sealed class SagaConfigurationException : Exception
{
    public SagaConfigurationException()
    {
    }

    public SagaConfigurationException(string message)
        : base(message)
    {
    }

    public SagaConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
