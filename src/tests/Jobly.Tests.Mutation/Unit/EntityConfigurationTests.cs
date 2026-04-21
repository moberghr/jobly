using Jobly.Core.Data.Entities;
using Jobly.Tests.Mutation.Fixtures;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobly.Tests.Mutation.Unit;

[Collection<SqliteCollection>]
public class EntityConfigurationTests
{
    private readonly SqliteFixture _fixture;

    public EntityConfigurationTests(SqliteFixture fixture) => _fixture = fixture;

    [Fact]
    public void StatisticEntity_HasKeyProperty()
    {
        using var context = _fixture.CreateContext();

        var entityType = context.Model.FindEntityType(typeof(Statistic));
        entityType.ShouldNotBeNull();

        var primaryKey = entityType.FindPrimaryKey();
        primaryKey.ShouldNotBeNull();
        primaryKey.Properties.Count.ShouldBe(1);
        primaryKey.Properties[0].Name.ShouldBe("Key");
    }

    [Fact]
    public void StatisticEntity_HasValueProperty()
    {
        using var context = _fixture.CreateContext();

        var entityType = context.Model.FindEntityType(typeof(Statistic));
        entityType.ShouldNotBeNull();

        var valueProperty = entityType.FindProperty("Value");
        valueProperty.ShouldNotBeNull();
    }
}
