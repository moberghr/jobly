using Microsoft.CodeAnalysis;

namespace Jobly.SourceGenerator;

internal sealed class StreamRequestTypeInfo
{
    public StreamRequestTypeInfo(
        string requestFullName,
        string responseFullName,
        string handlerFullName,
        string wrapperFieldName,
        Accessibility requestAccessibility)
    {
        RequestFullName = requestFullName;
        ResponseFullName = responseFullName;
        HandlerFullName = handlerFullName;
        WrapperFieldName = wrapperFieldName;
        RequestAccessibility = requestAccessibility;
    }

    public string RequestFullName { get; }

    public string ResponseFullName { get; }

    public string HandlerFullName { get; }

    public string WrapperFieldName { get; }

    public Accessibility RequestAccessibility { get; }
}
