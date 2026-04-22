using System.Collections.Generic;

namespace Jobly.SourceGenerator;

internal sealed class JobTypeInfo
{
    public JobTypeInfo(
        string jobFullName,
        IReadOnlyList<string> handlerFullNames,
        string methodName,
        bool isMessage)
    {
        JobFullName = jobFullName;
        HandlerFullNames = handlerFullNames;
        MethodName = methodName;
        IsMessage = isMessage;
    }

    public string JobFullName { get; }

    /// <summary>
    /// All handlers for this type. Always one for <see cref="IsMessage"/> = false (IJob),
    /// one or more for <see cref="IsMessage"/> = true (IMessage pub/sub).
    /// </summary>
    public IReadOnlyList<string> HandlerFullNames { get; }

    public string MethodName { get; }

    /// <summary>
    /// True for IMessage (multiple handlers possible), false for IJob (single handler).
    /// </summary>
    public bool IsMessage { get; }
}
