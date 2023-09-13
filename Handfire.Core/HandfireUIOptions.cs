using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Handfire.Core
{
	public class HandfireUIOptions
	{
		public string RoutePrefix { get; set; } = "/handfire";
		public Func<Stream> IndexStream { get; set; } = () => typeof(HandfireUIOptions).GetTypeInfo().Assembly
			.GetManifestResourceStream("Swashbuckle.AspNetCore.SwaggerUI.index.html");

	}
}
