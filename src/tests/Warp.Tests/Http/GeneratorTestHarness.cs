using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Warp.Core.Handlers;
using Warp.Http;
using Warp.Http.SourceGenerator;

namespace Warp.Tests.Http;

/// <summary>
/// Drives <see cref="WarpHttpGenerator"/> against ad-hoc source. Used by
/// <see cref="DiagnosticsTests"/> to exercise WHTTP rules without needing the
/// offending types to live in the main test compilation.
/// </summary>
internal static class GeneratorTestHarness
{
    public static GeneratorDriverRunResult Run(string sourceCode)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToImmutableArray();

        // Make sure the assemblies that define IRequest, IJob, IMessage, and the Warp.Http
        // attributes are reachable from the harness's compilation. The AppDomain scan above
        // typically picks them up because the test project references them, but be explicit.
        var explicitRefs = new[]
        {
            typeof(IRequest<>).Assembly,
            typeof(WarpHttpAttribute).Assembly,
        };
        foreach (var asm in explicitRefs)
        {
            if (!references.Any(r => string.Equals(r.Display, asm.Location, StringComparison.OrdinalIgnoreCase)))
            {
                references = references.Add(MetadataReference.CreateFromFile(asm.Location));
            }
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorHarness",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new WarpHttpGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        return driver.RunGenerators(compilation).GetRunResult();
    }
}
