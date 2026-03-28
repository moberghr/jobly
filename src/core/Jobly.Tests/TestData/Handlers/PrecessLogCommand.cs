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

    public async Task HandleAsync(PrecessLogRequest message, CancellationToken ct)
    {
        var testTask = await _context.TestLogs
            .Where(x => x.Id == message.TestTaskId)
            .FirstAsync(ct);

        testTask.ProcessedTime = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
    }
}

public class PrecessLogRequest : IJob
{
    public int TestTaskId { get; set; }
}
