using System.Data;
using System.Data.Common;
using Jobly.Core.Interceptors;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;

namespace Jobly.Tests.Unit;

[Trait("Category", "NoDb")]
public class SqlServerRowLockInterceptorTests
{
    private readonly SqlServerRowLockInterceptor _interceptor = new();

    [TimedFact]
    public void ManipulateCommand_WithDefaultTableName_AddsReadPastHint()
    {
        var command = CreateCommand($"""
            -- {InterceptorConstants.RowLockTableJob}
            SELECT TOP(1) [j].[Id], [j].[CurrentState]
            FROM [Job] AS [j]
            WHERE [j].[Kind] = 1
            """);

        _interceptor.ReaderExecuting(command, null!, default);

        command.CommandText.ShouldContain("FROM [Job] AS [j] WITH (ROWLOCK, UPDLOCK, READPAST)");
    }

    [TimedFact]
    public void ManipulateCommand_WithSchemaQualifiedTableName_AddsReadPastHint()
    {
        var command = CreateCommand($"""
            -- {InterceptorConstants.RowLockTableJob}
            SELECT TOP(1) [j].[Id], [j].[CurrentState]
            FROM [jobly].[Job] AS [j]
            WHERE [j].[Kind] = 1
            """);

        _interceptor.ReaderExecuting(command, null!, default);

        command.CommandText.ShouldContain("FROM [jobly].[Job] AS [j] WITH (ROWLOCK, UPDLOCK, READPAST)");
    }

    [TimedFact]
    public void ManipulateCommand_WithWaitTag_AddsUpdLockWithoutReadPast()
    {
        var command = CreateCommand($"""
            -- {InterceptorConstants.RowLockTableJobWait}
            SELECT TOP(1) [j].[Id]
            FROM [jobly].[Job] AS [j]
            WHERE [j].[Id] = @p0
            """);

        _interceptor.ReaderExecuting(command, null!, default);

        command.CommandText.ShouldContain("FROM [jobly].[Job] AS [j] WITH (ROWLOCK, UPDLOCK)");
        command.CommandText.ShouldNotContain("READPAST");
    }

    [TimedFact]
    public void ManipulateCommand_WithCounterTag_AddsReadPastHint()
    {
        var command = CreateCommand($"""
            -- {InterceptorConstants.RowLockTableCounter}
            SELECT TOP(1) [c].[Id], [c].[Key]
            FROM [jobly].[Counter] AS [c]
            WHERE [c].[Key] = @p0
            """);

        _interceptor.ReaderExecuting(command, null!, default);

        command.CommandText.ShouldContain("FROM [jobly].[Counter] AS [c] WITH (ROWLOCK, UPDLOCK, READPAST)");
    }

    private static FakeDbCommand CreateCommand(string commandText)
    {
        return new FakeDbCommand { CommandText = commandText };
    }

    private sealed class FakeDbCommand : DbCommand
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; }

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; }

        protected override DbParameterCollection DbParameterCollection => null!;

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel()
        {
        }

        public override int ExecuteNonQuery() => 0;

        public override object? ExecuteScalar() => null;

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter() => null!;

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => null!;
    }
}
