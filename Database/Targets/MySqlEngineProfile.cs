using Database.Models;
using Database.Repositories;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Database.Targets;

public sealed class MySqlEngineProfile : TargetEngineProfile {
    public MySqlEngineProfile(ILoggerFactory loggerFactory, bool debugQuery = false)
        : base(loggerFactory, debugQuery) { }

    public override TargetDatabaseEngine Engine => TargetDatabaseEngine.MySql;

    public const string SslModeOption = "sslMode";

    public override IReadOnlyList<FieldDefinition> Fields { get; } = [
        new() { Name = FieldNames.Host, Label = "Host", Kind = FieldKind.String, Required = true },
        new() { Name = FieldNames.Port, Label = "Port", Kind = FieldKind.Int, Required = true, Default = "3306" },
        new() { Name = FieldNames.DatabaseName, Label = "Database", Kind = FieldKind.String, Required = true },
        new() { Name = FieldNames.Username, Label = "Username", Kind = FieldKind.String, Required = true },
        new() { Name = FieldNames.Password, Label = "Password", Kind = FieldKind.Secret, Required = true },
        new() {
            Name = SslModeOption, Label = "SSL mode", Kind = FieldKind.Choice, Default = nameof(MySqlSslMode.Preferred),
            Choices = Enum.GetNames<MySqlSslMode>()
        }
    ];

    public override string BuildConnectionString(TargetConnectionDetails details) =>
        new MySqlConnectionStringBuilder {
            Server = details.Host,
            Port = (uint)(details.Port ?? 3306),
            Database = details.DatabaseName,
            UserID = details.Username,
            Password = details.Password,
            SslMode = details.Options.TryGetValue(SslModeOption, out var sslMode)
                ? Enum.Parse<MySqlSslMode>(sslMode, ignoreCase: true)
                : MySqlSslMode.Preferred
        }.ConnectionString;

    public override IRepository CreateRepository(string connectionString) =>
        new MySqlRepository(LoggerFactory.CreateLogger<MySqlRepository>(), connectionString, DebugQuery);
}
