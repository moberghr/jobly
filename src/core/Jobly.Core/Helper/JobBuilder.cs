using System.Text.Json;
using Jobly.Core.Enums;

namespace Jobly.Core.Helper;

public class JobParameters
{
    internal string Message { get; set; }
    internal string Type { get; set; }
    public int Retries { get; set; }
    public DateTime? ScheduleTime { get; set; }
    public int? MaxRetries { get; set; }
    public Priority? Priority { get; set; }
    public Guid? ParentId { get; set; }
    public int? RecurringJobId { get; set; }
    public State? State { get; set; }
}

public class JobBuilder
{
    private IPublisher _publisher;
    
    internal JobBuilder(IPublisher publisher)
    {
        _publisher = publisher;
    }

    public JobBuilder()
    {
    }

    public InnerBuilder WithMessage(object message)
    {
        var _message = JsonSerializer.Serialize(message);
        var type = message.GetType().AssemblyQualifiedName;
        return new InnerBuilder(_message, type);
    }

    internal InnerBuilder WithMessageAndType(string message, string type)
    {
        return new InnerBuilder(message, type);
    }
    
    public class InnerBuilder
    {
        private JobParameters _job;
        private IPublisher _publisher;
        
        internal InnerBuilder(IPublisher publisher, string message, string type)
        {
            _publisher = publisher;
            _job = new JobParameters
            {
                Message = message,
                Type = type
            };
        }
        
        internal InnerBuilder(string message, string type)
        {
            _job = new JobParameters
            {
                Message = message,
                Type = type
            };
        }
        

        public InnerBuilder WithRetries(int retries)
        {
            _job.Retries = retries;
            return this;
        }

        public InnerBuilder WithScheduleTime(DateTime? scheduleTime)
        {
            _job.ScheduleTime = scheduleTime;
            return this;
        }

        public InnerBuilder WithMaxRetries(int? maxRetries)
        {
            _job.MaxRetries = maxRetries;
            return this;
        }

        public InnerBuilder WithPriority(Priority? priority)
        {
            _job.Priority = priority;
            return this;
        }

        public InnerBuilder WithParentId(Guid? parentId)
        {
            _job.ParentId = parentId;
            return this;
        }

        public InnerBuilder WithRecurringJobId(int? recurringJobId)
        {
            _job.RecurringJobId = recurringJobId;
            return this;
        }

        public InnerBuilder WithState(State? state)
        {
            _job.State = state;
            return this;
        }

        internal JobParameters Build()
        {
            return _job;
        }
        
        public async Task<Guid> Publish()
        {
            return await _publisher.Publish(_job);
        }
    }
}