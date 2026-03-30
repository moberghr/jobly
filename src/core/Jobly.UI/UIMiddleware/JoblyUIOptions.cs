using System.Reflection;

namespace Jobly.UI.UIMiddleware;

public class JoblyUIOptions
{
    public string RoutePrefix { get; set; } = "/jobly";

    public Func<Stream> IndexStream { get; set; } = () => typeof(JoblyUIOptions).GetTypeInfo().Assembly.GetManifestResourceStream("Jobly.UI.dist.index.html")!;
}
