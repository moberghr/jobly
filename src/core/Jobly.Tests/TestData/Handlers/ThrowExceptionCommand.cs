using Jobly.Core.Handlers;

namespace Jobly.Tests.TestData.Handlers;
public class ThrowExceptionCommand : IJobHandler<ThrowExceptionRequest>
{
    public async Task HandleAsync(ThrowExceptionRequest message, CancellationToken ct)
    {
        throw new NotImplementedException();
    }
}

public class ThrowExceptionRequest : IJob
{

}
