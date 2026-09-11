using Database.Models;
using Database.Repositories;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Database.Targets;

/// <summary>
/// Published as unavailable for the same reason as <see cref="SqlServerEngineProfile"/>.
/// </summary>
public sealed class MySqlEngineProfile : TargetEngineProfile {
    public MySqlEngineProfile(ILoggerFactory loggerFactory, bool debugQuery = false)
        : base(loggerFactory, debugQuery) { }

    public override TargetDatabaseEngine Engine => TargetDatabaseEngine.MySql;

    public override bool IsAvailable => false;
    public override string? UnavailableReason => "The MySQL driver is not implemented yet.";

    public override IReadOnlyList<FieldDefinition> Fields { get; } = [
        new() { Name = FieldNames.Host, Label = "Host", Kind = FieldKind.String, Required = true },
        new() { Name = FieldNames.Port, Label = "Port", Kind = FieldKind.Int, Required = true, Default = "3306" },
        new() { Name = FieldNames.DatabaseName, Label = "Database", Kind = FieldKind.String, Required = true },
        new() { Name = FieldNames.Username, Label = "Username", Kind = FieldKind.String, Required = true },
        new() { Name = FieldNames.Password, Label = "Password", Kind = FieldKind.Secret, Required = true }
    ];

    public override string BuildConnectionString(TargetConnectionDetails details) =>
        new MySqlConnectionStringBuilder {
            Server = details.Host,
            Port = (uint)(details.Port ?? 3306),
            Database = details.DatabaseName,
            UserID = details.Username,
            Password = details.Password
        }.ConnectionString;

    public override IRepository CreateRepository(string connectionString) =>
        new MySqlRepository(LoggerFactory.CreateLogger<MySqlRepository>(), connectionString, DebugQuery);
}
