using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Warp.Core;

public static class WarpBuilderExtensions
{
    /// <summary>
    /// Binds a configuration section onto the builder's config fields (inherited from
    /// <see cref="WarpConfiguration"/> / <c>WarpWorkerConfiguration</c>). Intended for the
    /// appsettings.json pattern:
    /// <code>
    /// services.AddWarpWorker&lt;AppDbContext&gt;(opt =>
    /// {
    ///     opt.BindConfiguration(builder.Configuration.GetSection("Warp"));
    ///     opt.UsePostgreSql();
    /// });
    /// </code>
    /// Call order matters if both bind and explicit property assignment are used — the last
    /// write wins. Provider opt-in (<c>UsePostgreSql()</c> / <c>UseSqlServer()</c>) must still
    /// be called explicitly; it's a DI service registration, not a config field.
    /// </summary>
    public static IWarpBuilder<TContext> BindConfiguration<TContext>(
        this IWarpBuilder<TContext> builder,
        IConfiguration section)
        where TContext : DbContext
    {
        section.Bind(builder.Configuration);
        return builder;
    }
}
