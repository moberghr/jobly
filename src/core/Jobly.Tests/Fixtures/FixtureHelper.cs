using Jobly.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Jobly.Tests.Fixtures;

internal static class FixtureHelper
{
    internal static Respawn.Graph.Table[] GetServerTablesToIgnore(DbContext context)
    {
        var model = context.Model;

        return
        [
            ToRespawnTable(model, typeof(Server)),
            ToRespawnTable(model, typeof(Jobly.Core.Data.Entities.Worker)),
            ToRespawnTable(model, typeof(WorkerGroup)),
            ToRespawnTable(model, typeof(ServerTask)),
            ToRespawnTable(model, typeof(ServerLog)),
        ];
    }

    private static Respawn.Graph.Table ToRespawnTable(Microsoft.EntityFrameworkCore.Metadata.IModel model, Type entityType)
    {
        var entityModel = model.FindEntityType(entityType)!;
        var tableName = entityModel.GetTableName()!;
        var schema = entityModel.GetSchema();

        return schema != null
            ? new Respawn.Graph.Table(schema, tableName)
            : new Respawn.Graph.Table(tableName);
    }
}
