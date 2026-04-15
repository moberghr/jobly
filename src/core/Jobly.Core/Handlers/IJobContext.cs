using System.Diagnostics.CodeAnalysis;

namespace Jobly.Core.Handlers;

public interface IJobContext
{
    Guid JobId { get; }

    Guid TraceId { get; }

    JobFailureOutcome? FailureOutcome { get; set; }

    Dictionary<string, object> Metadata { get; }
}

[SuppressMessage("Design", "S3246:Generic type parameters should be co/contravariant when possible", Justification = "T is used as return type but covariance not possible due to FailureOutcome setter on base interface")]
public interface IJobContext<T> : IJobContext
    where T : IJobMetadata
{
    new T Metadata { get; }
}

public class JobContext : IJobContext
{
    public Guid JobId { get; set; }

    public Guid TraceId { get; set; }

    public JobFailureOutcome? FailureOutcome { get; set; }

    public Dictionary<string, object> Metadata { get; set; } = [];
}

public class JobContext<T> : IJobContext<T>
    where T : class, IJobMetadata
{
    private readonly JobContext _inner;
    private T? _typedMetadata;

    public JobContext(JobContext inner)
    {
        _inner = inner;
    }

    public Guid JobId => _inner.JobId;

    public Guid TraceId => _inner.TraceId;

    public JobFailureOutcome? FailureOutcome
    {
        get => _inner.FailureOutcome;
        set => _inner.FailureOutcome = value;
    }

    Dictionary<string, object> IJobContext.Metadata => _inner.Metadata;

    public T Metadata
    {
        get
        {
            if (_typedMetadata != null)
            {
                return _typedMetadata;
            }

            _typedMetadata = MetadataFactory.Create<T>(_inner.Metadata);
            _inner.Metadata = (Dictionary<string, object>)(object)_typedMetadata;

            return _typedMetadata;
        }
    }
}
