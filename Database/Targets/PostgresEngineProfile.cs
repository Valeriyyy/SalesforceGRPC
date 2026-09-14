using Database.Models;
using Database.Repositories;
using Database.Repositories.Interfaces;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Database.Targets;

public sealed class PostgresEngineProfile : TargetEngineProfile {
    public PostgresEngineProfile(ILoggerFactory loggerFactory, bool debugQuery = false)
        : base(loggerFactory, debugQuery) { }

    public override TargetDatabaseEngine Engine => TargetDatabaseEngine.Postgres;

    public override IReadOnlyList<FieldDefinition> Fields { get; } = [
        new() { Name = FieldNames.Host, Label = "Host", Kind = FieldKind.String, Required = true },
        new() { Name = FieldNames.Port, Label = "Port", Kind = FieldKind.Int, Required = true, Default = "5432" },
        new() { Name = FieldNames.DatabaseName, Label = "Database", Kind = FieldKind.String, Required = true },
        new() { Name = FieldNames.Username, Label = "Username", Kind = FieldKind.String, Required = true },
        new() { Name = FieldNames.Password, Label = "Password", Kind = FieldKind.Secret, Required = true },
        // Managed Postgres (RDS, Azure, Neon) commonly requires this. A choice rather than free text, so a
        // typo is a validation error here and not a parse error from the driver.
        new() {
            Name = SslModeOption, Label = "SSL mode", Kind = FieldKind.Choice, Default = nameof(SslMode.Prefer),
            Choices = Enum.GetNames<SslMode>()
        }
    ];

    public const string SslModeOption = "sslMode";

    public override string BuildConnectionString(TargetConnectionDetails details) =>
        new NpgsqlConnectionStringBuilder {
            Host = details.Host,
            Port = details.Port ?? 5432,
            Database = details.DatabaseName,
            Username = details.Username,
            Password = details.Password,
            SslMode = details.Options.TryGetValue(SslModeOption, out var sslMode)
                ? Enum.Parse<SslMode>(sslMode, ignoreCase: true)
                : SslMode.Prefer
        }.ConnectionString;

    public override IRepository CreateRepository(string connectionString) =>
        new PostgresRepository(LoggerFactory.CreateLogger<PostgresRepository>(), connectionString, DebugQuery);
}
