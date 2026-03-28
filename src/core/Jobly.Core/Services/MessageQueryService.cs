using Jobly.Core.Data.Entities;
using Jobly.Core.Entities;
using Jobly.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Core.Services;

public interface IMessageQueryService
{
    Task<PagedList<MessageModel>> GetMessages(BaseListRequest request);

    Task<MessageDetailModel?> GetMessageById(Guid messageId);
}

public class MessageQueryService<TContext> : IMessageQueryService
    where TContext : DbContext
{
    private readonly TContext _context;

    public MessageQueryService(TContext context)
    {
        _context = context;
    }

    public async Task<PagedList<MessageModel>> GetMessages(BaseListRequest request)
    {
        return await _context.Set<Message>()
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

        message.Jobs = await _context.Set<Job>()
            .Where(x => x.MessageId == messageId)
            .Select(x => new JobModel
            {
                Id = x.Id,
                Type = x.Type,
                Message = x.Message,
                CreateTime = x.CreateTime,
                ScheduleTime = x.ScheduleTime,
                CurrentState = x.CurrentState,
            })
            .ToListAsync();

        return message;
    }
}
