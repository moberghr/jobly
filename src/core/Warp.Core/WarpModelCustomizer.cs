using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Warp.Core;

internal sealed class WarpModelCustomizer : RelationalModelCustomizer
{
    public WarpModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies)
    {
    }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        var config = context.GetService<IOptions<WarpConfiguration>>()?.Value;
        var schema = config != null ? config.Schema : "warp";
        modelBuilder.AddOutboxStateEntity(schema);

        if (config != null)
        {
            foreach (var configurator in config.EntityConfigurators)
            {
                configurator(modelBuilder, schema);
            }
        }
    }
}
