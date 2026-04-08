namespace Jobly.SourceGenerator;

internal sealed class JobTypeInfo
{
    public JobTypeInfo(
        string jobFullName,
        string handlerFullName,
        string methodName,
        bool isMessage)
    {
        JobFullName = jobFullName;
        HandlerFullName = handlerFullName;
        MethodName = methodName;
        IsMessage = isMessage;
    }

    public string JobFullName { get; }

    public string HandlerFullName { get; }

    public string MethodName { get; }

    /// <summary>
    /// True for IMessage (multiple handlers possible), false for IJob (single handler).
    /// </summary>
    public bool IsMessage { get; }
}
