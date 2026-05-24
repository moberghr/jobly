using Microsoft.CodeAnalysis;

namespace Warp.Http.SourceGenerator;

internal static class Diagnostics
{
    private const string Category = "Warp.Http";

    public static readonly DiagnosticDescriptor InvalidHandler = new(
        id: "WHTTP001",
        title: "Type tagged with [WarpHttp...] must be a request/stream handler with an HTTP-eligible request",
        messageFormat: "Type '{0}' is tagged with a Warp.Http attribute but is not a valid handler: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingNameOnMultiAttribute = new(
        id: "WHTTP002",
        title: "Multi-attribute Warp.Http handlers require explicit Name on each attribute",
        messageFormat: "Handler '{0}' has multiple [WarpHttp...] attributes. Each must specify Name = \"...\" so the resulting ASP.NET endpoints have unique route names.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // WHTTP003 (FromBody on GET/DELETE) was removed when binding switched to ASP.NET Minimal API.
    // ASP.NET surfaces the equivalent error at runtime / startup.
    public static readonly DiagnosticDescriptor MultipleBodyTargets = new(
        id: "WHTTP004",
        title: "Body-verb handler has more than one body-bound parameter",
        messageFormat: "Handler '{0}' on a POST/PUT/PATCH route has multiple body-bound parameters. Minimal API accepts at most one body parameter. Wrap them in a single [FromBody] sub-record, or annotate each parameter with [FromRoute], [FromQuery], or [FromHeader].",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InternalGeneratorError = new(
        id: "WHTTP999",
        title: "Warp.Http source generator encountered an internal error",
        messageFormat: "Warp.Http source generator failed to process handler '{0}': {1}. Other handlers in the assembly are unaffected; please file an issue with a minimal repro.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
