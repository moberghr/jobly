using Jobly.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Core.Services;

public interface IServerCommandService
{
    Task<bool> PauseServer(Guid serverId);

    Task<bool> ResumeServer(Guid serverId);

    Task<bool> PauseWorkerGroup(Guid workerGroupId);

    Task<bool> ResumeWorkerGroup(Guid workerGroupId);
}

public class ServerCommandService<TContext> : IServerCommandService
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly TimeProvider _timeProvider;

    public ServerCommandService(TContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<bool> PauseServer(Guid serverId)
    {
        var server = await _context.Set<Server>().FindAsync(serverId);
        if (server == null)
        {
            return false;
        }

        server.PausedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ResumeServer(Guid serverId)
    {
        var server = await _context.Set<Server>().FindAsync(serverId);
        if (server == null)
        {
            return false;
        }

        server.PausedAt = null;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> PauseWorkerGroup(Guid workerGroupId)
    {
        var group = await _context.Set<WorkerGroup>().FindAsync(workerGroupId);
        if (group == null)
        {
            return false;
        }

        group.PausedAt = _timeProvider.GetUtcNow().UtcDateTime;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ResumeWorkerGroup(Guid workerGroupId)
    {
        var group = await _context.Set<WorkerGroup>().FindAsync(workerGroupId);
        if (group == null)
        {
            return false;
        }

        group.PausedAt = null;
        await _context.SaveChangesAsync();
        return true;
    }
}
