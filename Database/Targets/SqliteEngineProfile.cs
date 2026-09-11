using Database.Models;
using Database.Repositories;
using Database.Repositories.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Database.Targets;

public sealed class SqliteEngineProfile : TargetEngineProfile {
    public SqliteEngineProfile(ILoggerFactory loggerFactory, bool debugQuery = false)
        : base(loggerFactory, debugQuery) { }

    public override TargetDatabaseEngine Engine => TargetDatabaseEngine.Sqlite;

    public override IReadOnlyList<FieldDefinition> Fields { get; } = [
        new() { Name = FieldNames.FilePath, Label = "Database file", Kind = FieldKind.String, Required = true }
    ];

    public override string BuildConnectionString(TargetConnectionDetails details) =>
        new SqliteConnectionStringBuilder { DataSource = details.FilePath }.ConnectionString;

    public override IRepository CreateRepository(string connectionString) =>
        new SqliteRepository(LoggerFactory.CreateLogger<SqliteRepository>(), connectionString, DebugQuery);
}
