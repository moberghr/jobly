using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Jobly.Core;

internal sealed class JoblyModelCustomizer : RelationalModelCustomizer
{
    public JoblyModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies)
    {
    }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        var config = context.GetService<IOptions<JoblyConfiguration>>()?.Value;
        var schema = config != null ? config.Schema : "jobly";
        modelBuilder.AddOutboxStateEntity(schema);
    }
}
