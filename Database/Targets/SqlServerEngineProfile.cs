using Database.Models;
using Database.Repositories;
using Database.Repositories.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Database.Targets;

/// <summary>
/// Published as unavailable: <see cref="SqlServerRepository"/> throws from every method, and the save-time
/// proof reads schema metadata, so without this flag a user would enter a host and credentials and be told
/// their database failed.
/// </summary>
public sealed class SqlServerEngineProfile : TargetEngineProfile {
    public SqlServerEngineProfile(ILoggerFactory loggerFactory, bool debugQuery = false)
        : base(loggerFactory, debugQuery) { }

    public override TargetDatabaseEngine Engine => TargetDatabaseEngine.SqlServer;

    public override bool IsAvailable => false;
    public override string? UnavailableReason => "The SQL Server driver is not implemented yet.";

    public override IReadOnlyList<FieldDefinition> Fields { get; } = [
        new() { Name = FieldNames.Host, Label = "Server", Kind = FieldKind.String, Required = true },
        new() { Name = FieldNames.Port, Label = "Port", Kind = FieldKind.Int, Required = true, Default = "1433" },
        new() { Name = FieldNames.DatabaseName, Label = "Database", Kind = FieldKind.String, Required = true },
        new() { Name = FieldNames.Username, Label = "Username", Kind = FieldKind.String, Required = true },
        new() { Name = FieldNames.Password, Label = "Password", Kind = FieldKind.Secret, Required = true }
    ];

    public override string BuildConnectionString(TargetConnectionDetails details) =>
        new SqlConnectionStringBuilder {
            DataSource = $"{details.Host},{details.Port ?? 1433}",
            InitialCatalog = details.DatabaseName,
            UserID = details.Username,
            Password = details.Password
        }.ConnectionString;

    public override IRepository CreateRepository(string connectionString) =>
        new SqlServerRepository(LoggerFactory.CreateLogger<SqlServerRepository>(), connectionString, DebugQuery);
}
