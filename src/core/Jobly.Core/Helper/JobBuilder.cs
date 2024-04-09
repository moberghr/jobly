using System.Text.Json;
using Jobly.Core.Entities;
using Jobly.Core.Enums;

namespace Jobly.Core.Helper;

public class JobBuilder
{
    private string _message;
    private string _type;
    private int _retries;
    private DateTime? _scheduleTime;
    private int? _maxRetries;
    private Priority? _priority;
    private Guid? _parentId;
    private int? _recurringJobId;
    private State? _state;

    public JobBuilder WithMessage<T>(T message)
    {
        _message = JsonSerializer.Serialize(message);
        _type = message.GetType().AssemblyQualifiedName;
        return this;
    }
    
    public JobBuilder WithMessageAndType(string message, string type)
    {
        _message = message;
        _type = type;
        return this;
    }

    public JobBuilder WithRetries(int retries)
    {
        _retries = retries;
        return this;
    }

    public JobBuilder WithScheduleTime(DateTime? scheduleTime)
    {
        _scheduleTime = scheduleTime;
        return this;
    }

    public JobBuilder WithMaxRetries(int? maxRetries)
    {
        _maxRetries = maxRetries;
        return this;
    }

    public JobBuilder WithPriority(Priority? priority)
    {
        _priority = priority;
        return this;
    }

    public JobBuilder WithParentId(Guid? parentId)
    {
        _parentId = parentId;
        return this;
    }

    public JobBuilder WithRecurringJobId(int? recurringJobId)
    {
        _recurringJobId = recurringJobId;
        return this;
    }

    public JobBuilder WithState(State? state)
    {
        _state = state;
        return this;
    }

    public JobState Build()
    {
        var createdTime = DateTime.UtcNow;

        var job = new Job
        {
            CreateTime = createdTime,
            Message = _message,
            Type = _type,
            ScheduleTime = _scheduleTime ?? createdTime,
            CurrentState = _state ?? (_parentId == null ? State.Enqueued : State.Awaiting),
            MaxRetries = _maxRetries ?? _retries,
            Priority = _priority ?? Priority.Normal,
            ParentJobId = _parentId,
            RecurringJobId = _recurringJobId,
        };

        var jobState = new JobState
        {
            Job = job,
            State = State.Enqueued,
            DateTime = createdTime,
        };

        return jobState;
    }
}