using Microsoft.EntityFrameworkCore;
using Shouldly;
using Warp.Core.Data.Entities;

namespace Warp.Tests.Core;

[Trait("Category", "NoDb")]
public class UtcDateTimeConverterNamespaceFilterTests
{
    [TimedFact]
    public void ApplyConvention_StampsConverterOnWarpEntityProperties()
    {
        var options = new DbContextOptionsBuilder<MixedNamespaceContext>()
            .UseSqlServer("Server=dummy")
            .Options;
        using var context = new MixedNamespaceContext(options);

        var serverEntity = context.Model.FindEntityType(typeof(Server))!;
        var startedTime = serverEntity.FindProperty(nameof(Server.StartedTime))!;
        var pausedAt = serverEntity.FindProperty(nameof(Server.PausedAt))!;

        startedTime.GetValueConverter().ShouldNotBeNull();
        pausedAt.GetValueConverter().ShouldNotBeNull();
    }

    [TimedFact]
    public void ApplyConvention_LeavesNonWarpEntityPropertiesUntouched()
    {
        var options = new DbContextOptionsBuilder<MixedNamespaceContext>()
            .UseSqlServer("Server=dummy")
            .Options;
        using var context = new MixedNamespaceContext(options);

        var userEntity = context.Model.FindEntityType(typeof(Acme.Domain.UserOrder))!;
        var placedAt = userEntity.FindProperty(nameof(Acme.Domain.UserOrder.PlacedAt))!;
        var shippedAt = userEntity.FindProperty(nameof(Acme.Domain.UserOrder.ShippedAt))!;

        placedAt.GetValueConverter().ShouldBeNull();
        shippedAt.GetValueConverter().ShouldBeNull();
    }
}
