using Microsoft.EntityFrameworkCore;
using Jobly.Core.Entities;
using Jobly.Core.Enums;
using Jobly.Core.Helper;

namespace Jobly.Core
{
    public static class PublisherExtension
    {
        public static async Task<Guid> Publish<T>(this DbContext context, T message,
            CancellationToken cancellationToken = default) where T : class
        {
            return await context.CreateJobAndJobState(message, null, null, null, null, cancellationToken);
        }

        public static async Task<Guid> Publish<T>(this DbContext context, T message, DateTime scheduleTime,
            CancellationToken cancellationToken = default)
            where T : class
        {
            return await context.CreateJobAndJobState(message, scheduleTime, null, null, null, cancellationToken);
        }

        public static async Task<Guid> Publish<T>(this DbContext context, T message, int maxRetries,
            CancellationToken cancellationToken = default) where T : class
        {
            return await context.CreateJobAndJobState(message, null, maxRetries, null, null, cancellationToken);
        }

        public static async Task<Guid> Publish<T>(this DbContext context, T message, int maxRetries, Priority priority,
            CancellationToken cancellationToken = default)
            where T : class
        {
            return await context.CreateJobAndJobState(message, null, maxRetries, priority, null, cancellationToken);
        }

        public static async Task<Guid> Publish<T>(this DbContext context, T message, DateTime scheduleTime,
            int maxRetries, CancellationToken cancellationToken = default) where T : class
        {
            return await context.CreateJobAndJobState(message, scheduleTime, maxRetries, null, null, cancellationToken);
        }

        public static async Task<Guid> Publish<T>(this DbContext context, T message, DateTime scheduleTime,
            int maxRetries, Priority priority, CancellationToken cancellationToken = default) where T : class
        {
            return await context.CreateJobAndJobState(message, scheduleTime, maxRetries, priority, null,
                cancellationToken);
        }

        public static async Task<Guid> Publish<T>(this DbContext context, T message, DateTime scheduleTime,
            int maxRetries, Guid parentId, CancellationToken cancellationToken = default) where T : class
        {
            return await context.CreateJobAndJobState(message, scheduleTime, maxRetries, null, parentId,
                cancellationToken);
        }

        public static Task<Guid> Publish<T>(this DbContext context, T message, DateTime scheduleTime, int maxRetries,
            Guid parentId, Priority priority, CancellationToken cancellationToken = default) where T : class
        {
            return context.CreateJobAndJobState(message, scheduleTime, maxRetries, priority, parentId,
                cancellationToken);
        }

        public static async Task<Guid> Publish<T>(this DbContext context, T message, Priority priority,
            CancellationToken cancellationToken = default)
            where T : class
        {
            return await context.CreateJobAndJobState(message, null, null, priority, null, cancellationToken);
        }

        public static async Task<Guid> Publish<T>(this DbContext context, T message, Guid parentId,
            CancellationToken cancellationToken = default)
            where T : class
        {
            return await context.CreateJobAndJobState(message, null, null, null, parentId, cancellationToken);
        }

        public static async Task<Guid> Publish<T>(this DbContext context, T message, Guid parentId, Priority priority,
            CancellationToken cancellationToken = default)
            where T : class
        {
            return await context.CreateJobAndJobState(message, null, null, priority, parentId, cancellationToken);
        }

        public static async Task<Guid> Publish<T>(this DbContext context, T message, DateTime scheduleTime,
            Guid parentId, CancellationToken cancellationToken = default)
            where T : class
        {
            return await context.CreateJobAndJobState(message, scheduleTime, null, null, parentId, cancellationToken);
        }

        public static async Task<Guid> Publish<T>(this DbContext context, T message, DateTime scheduleTime,
            Guid parentId, Priority priority, CancellationToken cancellationToken = default)
            where T : class
        {
            return await context.CreateJobAndJobState(message, scheduleTime, null, priority, parentId,
                cancellationToken);
        }

        public static async Task<Guid> Publish<T>(this DbContext context, T message, int maxRetries, Guid parentId,
            CancellationToken cancellationToken = default) where T : class
        {
            return await context.CreateJobAndJobState(message, null, maxRetries, null, parentId, cancellationToken);
        }

        public static async Task<Guid> Publish<T>(this DbContext context, T message, int maxRetries, Guid parentId,
            Priority priority, CancellationToken cancellationToken = default) where T : class
        {
            return await context.CreateJobAndJobState(message, null, maxRetries, priority, parentId, cancellationToken);
        }


        private static async Task<Guid> CreateJobAndJobState<T>(this DbContext context, T message,
            DateTime? scheduleTime, int? maxRetries,
            Priority? priority, Guid? parentId, CancellationToken cancellationToken = default)
            where T : class
        {
            var jobState = JobHelper.CreateJobAndJobState(message, 0, string.Empty, scheduleTime,
                maxRetries, priority, parentId, null);

            await context.Set<JobState>().AddAsync(jobState, cancellationToken);

            return jobState.JobId;
        }
    }
}