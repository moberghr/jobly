using Warp.Core.Handlers;

namespace Warp.Tests.TestData.Handlers;

public class GetGreetingRequest : IRequest<string>
{
    public string Name { get; set; } = string.Empty;
}

public class GetGreetingHandler : IRequestHandler<GetGreetingRequest, string>
{
    public Task<string> HandleAsync(GetGreetingRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult($"Hello, {request.Name}!");
    }
}
