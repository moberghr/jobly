using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Handfire.Tests.TestData.Handlers;
public class PrecessLogCommand : IRequestHandler<PrecessLogRequest, PrecessLogResponse>
{
    private readonly TestContext _context;

    public PrecessLogCommand(TestContext context)
    {
        _context = context;
    }

    public async Task<PrecessLogResponse> Handle(PrecessLogRequest request, CancellationToken cancellationToken)
    {
        var testTask = await _context.TestLogs
            .Where(x => x.Id == request.TestTaskId)
            .FirstAsync(cancellationToken);

        testTask.ProcessedTime = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return new();
    }
}

public class PrecessLogRequest : IRequest<PrecessLogResponse>
{
    public int TestTaskId { get; set; }
}

public class PrecessLogResponse
{

}