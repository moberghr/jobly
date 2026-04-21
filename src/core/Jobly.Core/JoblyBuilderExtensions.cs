using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Jobly.Core;

public static class JoblyBuilderExtensions
{
    /// <summary>
    /// Binds a configuration section onto the builder's config fields (inherited from
    /// <see cref="JoblyConfiguration"/> / <c>JoblyWorkerConfiguration</c>). Intended for the
    /// appsettings.json pattern:
    /// <code>
    /// services.AddJoblyWorker&lt;AppDbContext&gt;(opt =>
    /// {
    ///     opt.BindConfiguration(builder.Configuration.GetSection("Jobly"));
    ///     opt.UsePostgreSql();
    /// });
    /// </code>
    /// Call order matters if both bind and explicit property assignment are used — the last
    /// write wins. Provider opt-in (<c>UsePostgreSql()</c> / <c>UseSqlServer()</c>) must still
    /// be called explicitly; it's a DI service registration, not a config field.
    /// </summary>
    public static IJoblyBuilder<TContext> BindConfiguration<TContext>(
        this IJoblyBuilder<TContext> builder,
        IConfiguration section)
        where TContext : DbContext
    {
        section.Bind(builder.Configuration);
        return builder;
    }
}
