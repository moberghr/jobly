using Jobly.Core.Handlers;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Tests.TestData.Handlers;

public class PrecessLogCommand : IJobHandler<PrecessLogRequest>
{
    private readonly TestContext _context;

    public PrecessLogCommand(TestContext context)
    {
        _context = context;
    }

    public async Task HandleAsync(PrecessLogRequest message, CancellationToken cancellationToken)
    {
        var testTask = await _context.TestLogs
            .Where(x => x.Id == message.TestTaskId)
            .FirstAsync(cancellationToken);

        testTask.ProcessedTime = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
    }
}

public class PrecessLogRequest : IJob
{
    public int TestTaskId { get; set; }
}
