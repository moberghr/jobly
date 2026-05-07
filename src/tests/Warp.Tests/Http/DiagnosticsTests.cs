using Microsoft.CodeAnalysis;
using Shouldly;
using Warp.Tests.TestData;

namespace Warp.Tests.Http;

[Trait("Category", "NoDb")]
public sealed class DiagnosticsTests
{
    [TimedFact]
    public void WHTTP001_FiresWhenHandlerIsForIJobRequest()
    {
        const string source = """
            using Warp.Core.Handlers;
            using Warp.Http;

            namespace TestSamples;

            public sealed record WorkJob : IJob;

            [WarpHttpPost("/jobs/work")]
            public sealed class WorkJobHandler : IJobHandler<WorkJob>
            {
                public Task HandleAsync(WorkJob request, System.Threading.CancellationToken ct) => Task.CompletedTask;
            }
            """;

        var diagnostics = GeneratorTestHarness.Run(source).Diagnostics;

        // Either form is acceptable: either the handler is rejected because IJobHandler isn't a
        // request handler (WHTTP001 path 1), or — if the user added IRequestHandler too — the
        // request type's IJob marker triggers WHTTP001 (path 2).
        diagnostics.ShouldContain(d => d.Id == "WHTTP001" && d.GetMessage().Contains("WorkJobHandler"));
    }

    [TimedFact]
    public void WHTTP001_FiresWhenHandlerImplementsBothIRequestHandlerAndIJobRequest()
    {
        const string source = """
            using Warp.Core.Handlers;
            using Warp.Http;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestSamples;

            public sealed record DualImplJob : IJob, IRequest<Unit>;

            [WarpHttpPost("/dual")]
            public sealed class DualHandler : IRequestHandler<DualImplJob, Unit>
            {
                public Task<Unit> HandleAsync(DualImplJob request, CancellationToken ct) => Task.FromResult(Unit.Value);
            }
            """;

        var diagnostics = GeneratorTestHarness.Run(source).Diagnostics;

        diagnostics.ShouldContain(d =>
            d.Id == "WHTTP001"
            && d.Severity == DiagnosticSeverity.Error
            && d.GetMessage().Contains("DualHandler")
            && d.GetMessage().Contains("IJob"));
    }

    [TimedFact]
    public void WHTTP001_FiresWhenAttributeIsOnNonHandlerType()
    {
        const string source = """
            using Warp.Http;

            namespace TestSamples;

            [WarpHttpGet("/random")]
            public sealed class RandomBag
            {
                public string? Tag { get; set; }
            }
            """;

        var diagnostics = GeneratorTestHarness.Run(source).Diagnostics;

        diagnostics.ShouldContain(d => d.Id == "WHTTP001" && d.GetMessage().Contains("RandomBag"));
    }

    [TimedFact]
    public void WHTTP001_FiresWhenHandlerIsForIMessageRequest()
    {
        const string source = """
            using Warp.Core.Handlers;
            using Warp.Http;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestSamples;

            public sealed record NotifyMessage : IMessage, IRequest<Unit>;

            [WarpHttpPost("/messages/notify")]
            public sealed class NotifyHandler : IRequestHandler<NotifyMessage, Unit>
            {
                public Task<Unit> HandleAsync(NotifyMessage request, CancellationToken ct) => Task.FromResult(Unit.Value);
            }
            """;

        var diagnostics = GeneratorTestHarness.Run(source).Diagnostics;

        diagnostics.ShouldContain(d => d.Id == "WHTTP001" && d.GetMessage().Contains("IMessage"));
    }

    [TimedFact]
    public void WHTTP001_DoesNotFireForPureRequestHandler()
    {
        const string source = """
            using Warp.Core.Handlers;
            using Warp.Http;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestSamples;

            public sealed record CreateOrder(string Name) : IRequest<string>;

            [WarpHttpPost("/orders")]
            public sealed class CreateOrderHandler : IRequestHandler<CreateOrder, string>
            {
                public Task<string> HandleAsync(CreateOrder request, CancellationToken ct) => Task.FromResult(request.Name);
            }
            """;

        var diagnostics = GeneratorTestHarness.Run(source).Diagnostics;

        diagnostics.ShouldNotContain(d => d.Id == "WHTTP001");
    }

    [TimedFact]
    public void WHTTP001_DoesNotFireForPureStreamRequestHandler()
    {
        const string source = """
            using Warp.Core.Handlers;
            using Warp.Http;
            using System.Threading;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;

            namespace TestSamples;

            public sealed record OrderFeed : IStreamRequest<int>;

            [WarpHttpGet("/feed")]
            public sealed class OrderFeedHandler : IStreamRequestHandler<OrderFeed, int>
            {
                public async IAsyncEnumerable<int> HandleAsync(OrderFeed request, [EnumeratorCancellation] CancellationToken ct)
                {
                    yield return 1;
                    await System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;

        var diagnostics = GeneratorTestHarness.Run(source).Diagnostics;

        diagnostics.ShouldNotContain(d => d.Id == "WHTTP001");
    }

    [TimedFact]
    public void WHTTP002_FiresWhenMultiAttributeMissingNameOnAny()
    {
        const string source = """
            using Warp.Core.Handlers;
            using Warp.Http;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestSamples;

            public sealed record MultiNoName(string Tag) : IRequest<string>;

            [WarpHttpPost("/v1/x", Name = "X1")]
            [WarpHttpPost("/v2/x")]
            public sealed class MultiNoNameHandler : IRequestHandler<MultiNoName, string>
            {
                public Task<string> HandleAsync(MultiNoName request, CancellationToken ct) => Task.FromResult(request.Tag);
            }
            """;

        var diagnostics = GeneratorTestHarness.Run(source).Diagnostics;

        diagnostics.ShouldContain(d => d.Id == "WHTTP002" && d.GetMessage().Contains("MultiNoNameHandler"));
    }

    [TimedFact]
    public void WHTTP002_DoesNotFireWhenAllAttributesHaveName()
    {
        const string source = """
            using Warp.Core.Handlers;
            using Warp.Http;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestSamples;

            public sealed record MultiNamed(string Tag) : IRequest<string>;

            [WarpHttpPost("/v1/x", Name = "X1")]
            [WarpHttpPost("/v2/x", Name = "X2")]
            public sealed class MultiNamedHandler : IRequestHandler<MultiNamed, string>
            {
                public Task<string> HandleAsync(MultiNamed request, CancellationToken ct) => Task.FromResult(request.Tag);
            }
            """;

        var diagnostics = GeneratorTestHarness.Run(source).Diagnostics;

        diagnostics.ShouldNotContain(d => d.Id == "WHTTP002");
    }

    [TimedFact]
    public void WHTTP002_DoesNotFireForSingleAttribute()
    {
        const string source = """
            using Warp.Core.Handlers;
            using Warp.Http;
            using System.Threading;
            using System.Threading.Tasks;

            namespace TestSamples;

            public sealed record Single(string Tag) : IRequest<string>;

            [WarpHttpPost("/single")]
            public sealed class SingleHandler : IRequestHandler<Single, string>
            {
                public Task<string> HandleAsync(Single request, CancellationToken ct) => Task.FromResult(request.Tag);
            }
            """;

        var diagnostics = GeneratorTestHarness.Run(source).Diagnostics;

        diagnostics.ShouldNotContain(d => d.Id == "WHTTP002");
    }
}
