using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

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
        modelBuilder.AddOutboxStateEntity();
    }
}
