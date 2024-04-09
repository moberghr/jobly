using System.Text.Json;
using Jobly.Core.Entities;
using Jobly.Core.Enums;

namespace Jobly.Core.Helper;

public class JobBuilder
{
    private JobBuilder() { } // Hide the constructor

    public static JobBuilder Create() // Expose a public static factory method
    {
        return new JobBuilder();
    }
    public InnerBuilder WithMessage<T>(T message)
    {
        var _message = JsonSerializer.Serialize(message);
        var type = message.GetType().AssemblyQualifiedName;
        return new InnerBuilder(_message, type);
    }
    
    public InnerBuilder WithMessageAndType(string message, string type)
    {
        return new InnerBuilder(message, type);
    }
    
    public class InnerBuilder
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
        
        internal InnerBuilder(string message, string type)
        {
            _message = message;
            _type = type;
        }

    public InnerBuilder WithRetries(int retries)
    {
        _retries = retries;
        return this;
    }

    public InnerBuilder WithScheduleTime(DateTime? scheduleTime)
    {
        _scheduleTime = scheduleTime;
        return this;
    }

    public InnerBuilder WithMaxRetries(int? maxRetries)
    {
        _maxRetries = maxRetries;
        return this;
    }

    public InnerBuilder WithPriority(Priority? priority)
    {
        _priority = priority;
        return this;
    }

    public InnerBuilder WithParentId(Guid? parentId)
    {
        _parentId = parentId;
        return this;
    }

    public InnerBuilder WithRecurringJobId(int? recurringJobId)
    {
        _recurringJobId = recurringJobId;
        return this;
    }

    public InnerBuilder WithState(State? state)
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
}