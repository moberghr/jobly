using System.Reflection;

namespace Jobly.Core
{
    public class JoblyUIOptions
    {
        public string RoutePrefix { get; set; } = "/dashboard";

        public Func<Stream> IndexStream { get; set; } = () => typeof(JoblyUIOptions).GetTypeInfo().Assembly.GetManifestResourceStream("Jobly.Core.UI.index.html");

    }
}
