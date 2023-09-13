using System.Reflection;

namespace Handfire.Core
{
    public class HandfireUIOptions
    {
        public string RoutePrefix { get; set; } = "/dashboard";

        public Func<Stream> IndexStream { get; set; } = () => typeof(HandfireUIOptions).GetTypeInfo().Assembly.GetManifestResourceStream("Handfire.Core.UI.index.html");

    }
}
