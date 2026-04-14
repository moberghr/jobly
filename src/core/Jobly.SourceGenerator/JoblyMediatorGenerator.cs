using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Jobly.SourceGenerator;

[Generator]
public sealed class JoblyMediatorGenerator : IIncrementalGenerator
{
    private const string IRequestMetadataName = "Jobly.Core.Handlers.IRequest`1";
    private const string IJobMetadataName = "Jobly.Core.Handlers.IJob";
    private const string IMessageMetadataName = "Jobly.Core.Handlers.IMessage";
    private const string IRequestHandlerMetadataName = "Jobly.Core.Handlers.IRequestHandler`2";
    private const string IJobHandlerMetadataName = "Jobly.Core.Handlers.IJobHandler`1";
    private const string IMessageHandlerMetadataName = "Jobly.Core.Handlers.IMessageHandler`1";
    private const string IStreamRequestMetadataName = "Jobly.Core.Handlers.IStreamRequest`1";
    private const string IStreamRequestHandlerMetadataName = "Jobly.Core.Handlers.IStreamRequestHandler`2";
    private const string IPublishPipelineBehaviorMetadataName = "Jobly.Core.Handlers.IPublishPipelineBehavior`1";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                transform: static (ctx, ct) => ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct) as INamedTypeSymbol)
            .Where(static symbol => symbol is not null);

        var compilationAndClasses = context.CompilationProvider.Combine(classDeclarations.Collect());

        context.RegisterSourceOutput(compilationAndClasses, static (spc, source) => Execute(spc, source.Left, source.Right!));
    }

    private static void Execute(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<INamedTypeSymbol?> candidates)
    {
        var iRequestSymbol = compilation.GetTypeByMetadataName(IRequestMetadataName);
        var iJobSymbol = compilation.GetTypeByMetadataName(IJobMetadataName);
        var iMessageSymbol = compilation.GetTypeByMetadataName(IMessageMetadataName);
        var iRequestHandlerSymbol = compilation.GetTypeByMetadataName(IRequestHandlerMetadataName);
        var iJobHandlerSymbol = compilation.GetTypeByMetadataName(IJobHandlerMetadataName);
        var iMessageHandlerSymbol = compilation.GetTypeByMetadataName(IMessageHandlerMetadataName);

        var iStreamRequestSymbol = compilation.GetTypeByMetadataName(IStreamRequestMetadataName);
        var iStreamRequestHandlerSymbol = compilation.GetTypeByMetadataName(IStreamRequestHandlerMetadataName);

        if (iRequestSymbol is null || iRequestHandlerSymbol is null)
        {
            return;
        }

        var allHandlerMap = BuildHandlerMap(compilation, iRequestHandlerSymbol);
        var streamHandlerMap = iStreamRequestHandlerSymbol is not null
            ? BuildHandlerMap(compilation, iStreamRequestHandlerSymbol)
            : [];
        var jobHandlerMap = iJobHandlerSymbol is not null
            ? BuildSingleTypeHandlerMap(compilation, iJobHandlerSymbol)
            : [];
        var messageHandlerMap = iMessageHandlerSymbol is not null
            ? BuildSingleTypeHandlerMap(compilation, iMessageHandlerSymbol)
            : [];

        var iPublishBehaviorSymbol = compilation.GetTypeByMetadataName(IPublishPipelineBehaviorMetadataName);

        var requestTypes = new List<RequestTypeInfo>();
        var streamRequestTypes = new List<StreamRequestTypeInfo>();
        var jobTypes = new List<JobTypeInfo>();

        foreach (var candidate in candidates)
        {
            if (candidate is null || candidate.IsAbstract)
            {
                continue;
            }

            // Check if this type implements IRequest<T>
            INamedTypeSymbol? requestInterface = null;
#pragma warning disable S3267 // Loop with break is clearer than LINQ for early-exit pattern matching
            foreach (var iface in candidate.AllInterfaces)
#pragma warning restore S3267
            {
                if (iface.OriginalDefinition.Equals(iRequestSymbol, SymbolEqualityComparer.Default))
                {
                    requestInterface = iface;
                    break;
                }
            }

            var candidateFullName = GetFullyQualifiedName(candidate);

            if (requestInterface is not null)
            {
                // Check if it's an IJob type
                var isJob = iJobSymbol is not null && candidate.AllInterfaces.Any(i =>
                    i.Equals(iJobSymbol, SymbolEqualityComparer.Default));

                // Check if it's an IMessage type
                var isMessage = iMessageSymbol is not null && candidate.AllInterfaces.Any(i =>
                    i.Equals(iMessageSymbol, SymbolEqualityComparer.Default));

                if (isJob || isMessage)
                {
                    var handlerLookup = isJob ? jobHandlerMap : messageHandlerMap;
                    if (handlerLookup.TryGetValue(candidateFullName, out var handlerSymbol))
                    {
                        var methodName = "Execute_" + candidate.Name;
                        jobTypes.Add(new JobTypeInfo(
                            candidateFullName,
                            GetFullyQualifiedName(handlerSymbol),
                            methodName,
                            isMessage));
                    }

                    continue;
                }

                // It's a plain IRequest<TResponse> — mediator path
                var responseType = requestInterface.TypeArguments[0];
                var responseFullName = GetFullyQualifiedName(responseType);

                var handlerKey = candidateFullName + "|" + responseFullName;
                if (allHandlerMap.TryGetValue(handlerKey, out var reqHandlerSymbol))
                {
                    var wrapperFieldName = "_wrapper_" + candidate.Name;
                    requestTypes.Add(new RequestTypeInfo(
                        candidateFullName,
                        responseFullName,
                        GetFullyQualifiedName(reqHandlerSymbol),
                        wrapperFieldName,
                        candidate.DeclaredAccessibility));
                }

                continue;
            }

            // Check if this type implements IStreamRequest<T>
            if (iStreamRequestSymbol is not null)
            {
                INamedTypeSymbol? streamRequestInterface = null;
#pragma warning disable S3267
                foreach (var iface in candidate.AllInterfaces)
#pragma warning restore S3267
                {
                    if (iface.OriginalDefinition.Equals(iStreamRequestSymbol, SymbolEqualityComparer.Default))
                    {
                        streamRequestInterface = iface;
                        break;
                    }
                }

                if (streamRequestInterface is not null)
                {
                    var streamResponseType = streamRequestInterface.TypeArguments[0];
                    var streamResponseFullName = GetFullyQualifiedName(streamResponseType);

                    var streamHandlerKey = candidateFullName + "|" + streamResponseFullName;
                    if (streamHandlerMap.TryGetValue(streamHandlerKey, out var streamHandlerSymbol))
                    {
                        var wrapperFieldName = "_streamWrapper_" + candidate.Name;
                        streamRequestTypes.Add(new StreamRequestTypeInfo(
                            candidateFullName,
                            streamResponseFullName,
                            GetFullyQualifiedName(streamHandlerSymbol),
                            wrapperFieldName,
                            candidate.DeclaredAccessibility));
                    }
                }
            }
        }

        // Collect publish behavior registrations — multiple behaviors per type are allowed (pipeline chain)
        var publishBehaviorRegistrations = new List<(string BehaviorFullName, string InterfaceFullName)>();
        if (iPublishBehaviorSymbol is not null)
        {
            foreach (var type in GetAllTypes(compilation))
            {
                if (type.IsAbstract || type.TypeKind == TypeKind.Interface)
                {
                    continue;
                }

                foreach (var iface in type.AllInterfaces)
                {
                    if (!iface.OriginalDefinition.Equals(iPublishBehaviorSymbol, SymbolEqualityComparer.Default))
                    {
                        continue;
                    }

                    var behaviorFullName = GetFullyQualifiedName(type);
                    var messageTypeFullName = GetFullyQualifiedName(iface.TypeArguments[0]);
                    var interfaceFullName = $"global::Jobly.Core.Handlers.IPublishPipelineBehavior<{messageTypeFullName}>";
                    publishBehaviorRegistrations.Add((behaviorFullName, interfaceFullName));
                }
            }
        }

        if (requestTypes.Count == 0 && streamRequestTypes.Count == 0 && jobTypes.Count == 0 && publishBehaviorRegistrations.Count == 0)
        {
            return;
        }

        var source = GenerateSource(requestTypes, streamRequestTypes, jobTypes, publishBehaviorRegistrations);
        context.AddSource("JoblyMediator.g.cs", source);
    }

    private static Dictionary<string, INamedTypeSymbol> BuildHandlerMap(
        Compilation compilation,
        INamedTypeSymbol iRequestHandlerSymbol)
    {
        var map = new Dictionary<string, INamedTypeSymbol>();

        foreach (var type in GetAllTypes(compilation))
        {
            if (type.IsAbstract || type.TypeKind == TypeKind.Interface)
            {
                continue;
            }

            foreach (var iface in type.AllInterfaces)
            {
                if (!iface.OriginalDefinition.Equals(iRequestHandlerSymbol, SymbolEqualityComparer.Default))
                {
                    continue;
                }

                var requestType = iface.TypeArguments[0];
                var responseType = iface.TypeArguments[1];
                var key = GetFullyQualifiedName(requestType) + "|" + GetFullyQualifiedName(responseType);
                map[key] = type;
            }
        }

        return map;
    }

    /// <summary>
    /// Build handler map for single-type-param interfaces (IJobHandler&lt;T&gt;, IMessageHandler&lt;T&gt;).
    /// </summary>
    private static Dictionary<string, INamedTypeSymbol> BuildSingleTypeHandlerMap(
        Compilation compilation,
        INamedTypeSymbol handlerInterfaceSymbol)
    {
        var map = new Dictionary<string, INamedTypeSymbol>();

        foreach (var type in GetAllTypes(compilation))
        {
            if (type.IsAbstract || type.TypeKind == TypeKind.Interface)
            {
                continue;
            }

            foreach (var iface in type.AllInterfaces)
            {
                if (!iface.OriginalDefinition.Equals(handlerInterfaceSymbol, SymbolEqualityComparer.Default))
                {
                    continue;
                }

                var messageType = iface.TypeArguments[0];
                var key = GetFullyQualifiedName(messageType);
                map[key] = type;
            }
        }

        return map;
    }

    private static IEnumerable<INamedTypeSymbol> GetAllTypes(Compilation compilation)
    {
        var stack = new Stack<INamespaceOrTypeSymbol>();
        stack.Push(compilation.GlobalNamespace);

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
            {
                stack.Push(assembly.GlobalNamespace);
            }
        }

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is INamedTypeSymbol namedType)
            {
                yield return namedType;
                foreach (var nested in namedType.GetTypeMembers())
                {
                    stack.Push(nested);
                }
            }

            if (current is INamespaceSymbol ns)
            {
                foreach (var member in ns.GetMembers())
                {
                    if (member is INamespaceOrTypeSymbol nsOrType)
                    {
                        stack.Push(nsOrType);
                    }
                }
            }
        }
    }

    private static string GetFullyQualifiedName(ISymbol symbol)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static string GenerateSource(List<RequestTypeInfo> requestTypes, List<StreamRequestTypeInfo> streamRequestTypes, List<JobTypeInfo> jobTypes, List<(string BehaviorFullName, string InterfaceFullName)> publishBehaviorRegistrations)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Jobly.Core.Handlers;");
        sb.AppendLine();
        sb.AppendLine("namespace Jobly.Core.Handlers.Generated");
        sb.AppendLine("{");

        if (requestTypes.Count > 0 || streamRequestTypes.Count > 0)
        {
            GenerateMediatorCode(sb, requestTypes, streamRequestTypes);
        }

        if (jobTypes.Count > 0)
        {
            GenerateJobDispatcherCode(sb, jobTypes);
        }

        GenerateDIRegistration(sb, requestTypes, streamRequestTypes, jobTypes, publishBehaviorRegistrations);

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void GenerateMediatorCode(StringBuilder sb, List<RequestTypeInfo> requestTypes, List<StreamRequestTypeInfo> streamRequestTypes)
    {
        if (requestTypes.Count > 0)
        {
            // RequestHandlerWrapper<TRequest, TResponse>
            sb.AppendLine("    internal sealed class RequestHandlerWrapper<TRequest, TResponse>");
            sb.AppendLine("        where TRequest : global::Jobly.Core.Handlers.IRequest<TResponse>");
            sb.AppendLine("    {");
            sb.AppendLine("        private global::Jobly.Core.Handlers.RequestHandlerDelegate<TRequest, TResponse> _rootHandler = null!;");
            sb.AppendLine();
            sb.AppendLine("        public void Init(global::System.IServiceProvider sp)");
            sb.AppendLine("        {");
            sb.AppendLine("            var handler = sp.GetRequiredService<global::Jobly.Core.Handlers.IRequestHandler<TRequest, TResponse>>();");
            sb.AppendLine("            var behaviors = sp.GetServices<global::Jobly.Core.Handlers.IPipelineBehavior<TRequest, TResponse>>().ToArray();");
            sb.AppendLine("            global::Jobly.Core.Handlers.RequestHandlerDelegate<TRequest, TResponse> chain = handler.HandleAsync;");
            sb.AppendLine("            for (int i = behaviors.Length - 1; i >= 0; i--)");
            sb.AppendLine("            {");
            sb.AppendLine("                var b = behaviors[i];");
            sb.AppendLine("                var next = chain;");
            sb.AppendLine("                chain = (req, ct) => b.HandleAsync(req, next, ct);");
            sb.AppendLine("            }");
            sb.AppendLine("            _rootHandler = chain;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public global::System.Threading.Tasks.Task<TResponse> Handle(TRequest request, global::System.Threading.CancellationToken ct)");
            sb.AppendLine("            => _rootHandler(request, ct);");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        if (streamRequestTypes.Count > 0)
        {
            // StreamHandlerWrapper<TRequest, TResponse>
            sb.AppendLine("    internal sealed class StreamHandlerWrapper<TRequest, TResponse>");
            sb.AppendLine("        where TRequest : global::Jobly.Core.Handlers.IStreamRequest<TResponse>");
            sb.AppendLine("    {");
            sb.AppendLine("        private global::Jobly.Core.Handlers.StreamHandlerDelegate<TRequest, TResponse> _rootHandler = null!;");
            sb.AppendLine();
            sb.AppendLine("        public void Init(global::System.IServiceProvider sp)");
            sb.AppendLine("        {");
            sb.AppendLine("            var handler = sp.GetRequiredService<global::Jobly.Core.Handlers.IStreamRequestHandler<TRequest, TResponse>>();");
            sb.AppendLine("            var behaviors = sp.GetServices<global::Jobly.Core.Handlers.IStreamPipelineBehavior<TRequest, TResponse>>().ToArray();");
            sb.AppendLine("            global::Jobly.Core.Handlers.StreamHandlerDelegate<TRequest, TResponse> chain = handler.HandleAsync;");
            sb.AppendLine("            for (int i = behaviors.Length - 1; i >= 0; i--)");
            sb.AppendLine("            {");
            sb.AppendLine("                var b = behaviors[i];");
            sb.AppendLine("                var next = chain;");
            sb.AppendLine("                chain = (req, ct) => b.HandleAsync(req, next, ct);");
            sb.AppendLine("            }");
            sb.AppendLine("            _rootHandler = chain;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public global::System.Collections.Generic.IAsyncEnumerable<TResponse> Handle(TRequest request, global::System.Threading.CancellationToken ct)");
            sb.AppendLine("            => _rootHandler(request, ct);");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        // GeneratedMediator
        sb.AppendLine("    public sealed class GeneratedMediator : global::Jobly.Core.Handlers.IMediator");
        sb.AppendLine("    {");
        foreach (var req in requestTypes)
        {
            sb.AppendLine($"        private readonly RequestHandlerWrapper<{req.RequestFullName}, {req.ResponseFullName}> {req.WrapperFieldName};");
        }

        foreach (var stream in streamRequestTypes)
        {
            sb.AppendLine($"        private readonly StreamHandlerWrapper<{stream.RequestFullName}, {stream.ResponseFullName}> {stream.WrapperFieldName};");
        }

        sb.AppendLine();
        sb.AppendLine("        public GeneratedMediator(global::System.IServiceProvider sp)");
        sb.AppendLine("        {");
        foreach (var req in requestTypes)
        {
            sb.AppendLine($"            {req.WrapperFieldName} = new RequestHandlerWrapper<{req.RequestFullName}, {req.ResponseFullName}>();");
            sb.AppendLine($"            {req.WrapperFieldName}.Init(sp);");
        }

        foreach (var stream in streamRequestTypes)
        {
            sb.AppendLine($"            {stream.WrapperFieldName} = new StreamHandlerWrapper<{stream.RequestFullName}, {stream.ResponseFullName}>();");
            sb.AppendLine($"            {stream.WrapperFieldName}.Init(sp);");
        }

        sb.AppendLine("        }");
        sb.AppendLine();

        // Send overloads
        foreach (var req in requestTypes)
        {
            sb.AppendLine($"        public global::System.Threading.Tasks.Task<{req.ResponseFullName}> Send({req.RequestFullName} request, global::System.Threading.CancellationToken cancellationToken = default)");
            sb.AppendLine($"            => {req.WrapperFieldName}.Handle(request, cancellationToken);");
            sb.AppendLine();
        }

        sb.AppendLine("        public global::System.Threading.Tasks.Task<TResponse> Send<TResponse>(global::Jobly.Core.Handlers.IRequest<TResponse> request, global::System.Threading.CancellationToken cancellationToken = default)");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (request)");
        sb.AppendLine("            {");
        foreach (var req in requestTypes)
        {
            sb.AppendLine($"                case {req.RequestFullName} r:");
            sb.AppendLine($"                    return (global::System.Threading.Tasks.Task<TResponse>)(object)Send(r, cancellationToken);");
        }

        sb.AppendLine("                default:");
        sb.AppendLine("                    throw new global::System.InvalidOperationException($\"No handler registered for {request.GetType().Name}\");");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();

        // CreateStream overloads
        foreach (var stream in streamRequestTypes)
        {
            sb.AppendLine($"        public global::System.Collections.Generic.IAsyncEnumerable<{stream.ResponseFullName}> CreateStream({stream.RequestFullName} request, global::System.Threading.CancellationToken cancellationToken = default)");
            sb.AppendLine($"            => {stream.WrapperFieldName}.Handle(request, cancellationToken);");
            sb.AppendLine();
        }

        sb.AppendLine("        public global::System.Collections.Generic.IAsyncEnumerable<TResponse> CreateStream<TResponse>(global::Jobly.Core.Handlers.IStreamRequest<TResponse> request, global::System.Threading.CancellationToken cancellationToken = default)");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (request)");
        sb.AppendLine("            {");
        foreach (var stream in streamRequestTypes)
        {
            sb.AppendLine($"                case {stream.RequestFullName} r:");
            sb.AppendLine($"                    return (global::System.Collections.Generic.IAsyncEnumerable<TResponse>)(object)CreateStream(r, cancellationToken);");
        }

        sb.AppendLine("                default:");
        sb.AppendLine("                    throw new global::System.InvalidOperationException($\"No stream handler registered for {request.GetType().Name}\");");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateJobDispatcherCode(StringBuilder sb, List<JobTypeInfo> jobTypes)
    {
        sb.AppendLine("    public static class GeneratedJobDispatcher");
        sb.AppendLine("    {");

        // TryExecute — switches on messageType, returns null if not a known type
        sb.AppendLine("        public static global::System.Threading.Tasks.Task? TryExecute(");
        sb.AppendLine("            object message,");
        sb.AppendLine("            global::System.Type messageType,");
        sb.AppendLine("            global::System.Type handlerType,");
        sb.AppendLine("            global::System.IServiceProvider provider,");
        sb.AppendLine("            global::System.Threading.CancellationToken cancellationToken)");
        sb.AppendLine("        {");
        foreach (var job in jobTypes)
        {
            sb.AppendLine($"            if (messageType == typeof({job.JobFullName}))");
            sb.AppendLine($"                return {job.MethodName}(({job.JobFullName})message, handlerType, provider, cancellationToken);");
        }

        sb.AppendLine("            return null;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // Per-type execute methods
        foreach (var job in jobTypes)
        {
            sb.AppendLine($"        private static async global::System.Threading.Tasks.Task {job.MethodName}(");
            sb.AppendLine($"            {job.JobFullName} message,");
            sb.AppendLine($"            global::System.Type handlerType,");
            sb.AppendLine($"            global::System.IServiceProvider provider,");
            sb.AppendLine($"            global::System.Threading.CancellationToken cancellationToken)");
            sb.AppendLine("        {");

            if (job.IsMessage)
            {
                // IMessage: multiple handlers possible, resolve by handlerType
                sb.AppendLine($"            var allHandlers = provider.GetServices<global::Jobly.Core.Handlers.IMessageHandler<{job.JobFullName}>>();");
                sb.AppendLine("            var handler = allHandlers.First(h => h!.GetType() == handlerType);");
            }
            else
            {
                // IJob: single handler
                sb.AppendLine($"            var handler = provider.GetRequiredService<global::Jobly.Core.Handlers.IJobHandler<{job.JobFullName}>>();");
            }

            sb.AppendLine($"            var behaviors = provider.GetServices<global::Jobly.Core.Handlers.IPipelineBehavior<{job.JobFullName}, global::Jobly.Core.Handlers.Unit>>().ToArray();");
            sb.AppendLine();
            sb.AppendLine($"            global::Jobly.Core.Handlers.RequestHandlerDelegate<{job.JobFullName}, global::Jobly.Core.Handlers.Unit> chain = async (req, ct) =>");
            sb.AppendLine("            {");
            sb.AppendLine("                await handler.HandleAsync(req, ct);");
            sb.AppendLine("                return global::Jobly.Core.Handlers.Unit.Value;");
            sb.AppendLine("            };");
            sb.AppendLine();
            sb.AppendLine("            for (int i = behaviors.Length - 1; i >= 0; i--)");
            sb.AppendLine("            {");
            sb.AppendLine("                var b = behaviors[i];");
            sb.AppendLine("                var next = chain;");
            sb.AppendLine("                chain = (req, ct) => b.HandleAsync(req, next, ct);");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            await chain(message, cancellationToken);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine();
    }

    private static void GenerateDIRegistration(StringBuilder sb, List<RequestTypeInfo> requestTypes, List<StreamRequestTypeInfo> streamRequestTypes, List<JobTypeInfo> jobTypes, List<(string BehaviorFullName, string InterfaceFullName)> publishBehaviorRegistrations)
    {
        sb.AppendLine("    public static class JoblyMediatorServiceExtensions");
        sb.AppendLine("    {");
        sb.AppendLine("        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddJoblyMediator(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        sb.AppendLine("        {");

        foreach (var req in requestTypes)
        {
            sb.AppendLine($"            services.AddTransient<global::Jobly.Core.Handlers.IRequestHandler<{req.RequestFullName}, {req.ResponseFullName}>, {req.HandlerFullName}>();");
        }

        foreach (var stream in streamRequestTypes)
        {
            sb.AppendLine($"            services.AddTransient<global::Jobly.Core.Handlers.IStreamRequestHandler<{stream.RequestFullName}, {stream.ResponseFullName}>, {stream.HandlerFullName}>();");
        }

        foreach (var job in jobTypes)
        {
            if (job.IsMessage)
            {
                sb.AppendLine($"            services.AddTransient<global::Jobly.Core.Handlers.IMessageHandler<{job.JobFullName}>, {job.HandlerFullName}>();");
            }
            else
            {
                sb.AppendLine($"            services.AddTransient<global::Jobly.Core.Handlers.IJobHandler<{job.JobFullName}>, {job.HandlerFullName}>();");
            }
        }

        foreach (var (behaviorFullName, interfaceFullName) in publishBehaviorRegistrations)
        {
            sb.AppendLine($"            services.AddTransient<{interfaceFullName}, {behaviorFullName}>();");
        }

        if (requestTypes.Count > 0 || streamRequestTypes.Count > 0)
        {
            sb.AppendLine("            services.AddScoped<global::Jobly.Core.Handlers.IMediator, GeneratedMediator>();");
        }

        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }
}
