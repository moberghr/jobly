using System.Runtime.CompilerServices;
using Jobly.Core.Handlers;

namespace Jobly.Tests.TestData.Handlers;

public class GetNumbersStreamRequest : IStreamRequest<int>
{
    public int Count { get; set; }
}

public class GetNumbersStreamHandler : IStreamRequestHandler<GetNumbersStreamRequest, int>
{
    public async IAsyncEnumerable<int> HandleAsync(
        GetNumbersStreamRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return i;
            await Task.Yield();
        }
    }
}
