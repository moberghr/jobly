using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Core.Services;

public interface IMessageQueryService
{
    Task<PagedList<MessageModel>> GetMessages(BaseListRequest request, string? state = null);

    Task<MessageDetailModel?> GetMessageById(Guid messageId);

    Task<PagedList<JobModel>> GetMessageJobs(Guid messageId, BaseListRequest request, string? state = null);

    Task<Dictionary<string, int>> GetMessageJobCounts(Guid messageId);
}

public class MessageQueryService<TContext> : IMessageQueryService
    where TContext : DbContext
{
    private readonly TContext _context;

    public MessageQueryService(TContext context)
    {
        _context = context;
    }

    public async Task<PagedList<MessageModel>> GetMessages(BaseListRequest request, string? state = null)
    {
        var query = _context.Set<Message>().AsQueryable();

        query = state switch
        {
            "enqueued" => query.Where(x => x.CurrentState == State.Enqueued),
            "processing" => query.Where(x => x.CurrentState == State.Processing),
            "completed" => query.Where(x => x.CurrentState == State.Completed),
            "failed" => query.Where(x => x.CurrentState == State.Failed),
            _ => query,
        };

        return await query
            .OrderByDescending(x => x.CreateTime)
            .Select(x => new MessageModel
            {
                Id = x.Id,
                Type = x.Type,
                Payload = x.Payload,
                Queue = x.Queue,
                CurrentState = x.CurrentState,
                JobCount = x.JobCount,
                CreateTime = x.CreateTime,
            })
            .ToPagedListAsync(request);
    }

    public async Task<MessageDetailModel?> GetMessageById(Guid messageId)
    {
        var message = await _context.Set<Message>()
            .Where(x => x.Id == messageId)
            .Select(x => new MessageDetailModel
            {
                Id = x.Id,
                Type = x.Type,
                Payload = x.Payload,
                Queue = x.Queue,
                CurrentState = x.CurrentState,
                JobCount = x.JobCount,
                CreateTime = x.CreateTime,
            })
            .FirstOrDefaultAsync();

        if (message == null)
        {
            return null;
        }

        message.JobsCount = await _context.Set<Job>()
            .Where(x => x.MessageId == messageId)
            .CountAsync();

        return message;
    }

    public async Task<PagedList<JobModel>> GetMessageJobs(Guid messageId, BaseListRequest request, string? state = null)
    {
        var query = _context.Set<Job>()
            .Where(x => x.MessageId == messageId);

        query = state switch
        {
            "enqueued" => query.Where(x => x.CurrentState == State.Enqueued),
            "processing" => query.Where(x => x.CurrentState == State.Processing),
            "completed" => query.Where(x => x.CurrentState == State.Completed),
            "failed" => query.Where(x => x.CurrentState == State.Failed),
            _ => query,
        };

        return await query
            .OrderByDescending(x => x.CreateTime)
            .Select(x => new JobModel
            {
                Id = x.Id,
                Type = x.Type,
                Message = x.Message,
                CreateTime = x.CreateTime,
                ScheduleTime = x.ScheduleTime,
                CurrentState = x.CurrentState,
            })
            .ToPagedListAsync(request);
    }

    public async Task<Dictionary<string, int>> GetMessageJobCounts(Guid messageId)
    {
        var counts = await _context.Set<Job>()
            .Where(x => x.MessageId == messageId)
            .GroupBy(x => x.CurrentState)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync();

        return counts.ToDictionary(x => x.State.ToString().ToLowerInvariant(), x => x.Count);
    }
}
