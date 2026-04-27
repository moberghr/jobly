using Microsoft.CodeAnalysis;

namespace Warp.SourceGenerator;

internal sealed class RequestTypeInfo
{
    public RequestTypeInfo(
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
